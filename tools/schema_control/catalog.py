"""Deterministic PostgreSQL catalog extraction for Meridian schema control.

The extractor reads PostgreSQL's catalog after the registered migrations have
been applied. It intentionally omits OIDs, statistics, sizes, and timestamps
while retaining resolved ownership and access-control metadata needed for
security drift checks. ``psycopg`` is imported only for a real connection so
unit tests can inject small mapping-row fakes without installing a driver.
"""

from __future__ import annotations

import re
from collections import defaultdict
from collections.abc import Callable, Mapping, Sequence
from contextlib import contextmanager
from datetime import date, datetime, time
from decimal import Decimal
from pathlib import Path
from typing import Any
from uuid import UUID

from .common import fingerprint, normalize_text, normalize_value, sha256_text


ConnectionFactory = Callable[[str], Any]
_IDENTIFIER = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")


_SCHEMA_SQL = """
/* schema-control:schemas */
select
    namespace.nspname as schema_name,
    pg_catalog.pg_get_userbyid(namespace.nspowner) as owner,
    coalesce(
        (
            select jsonb_agg(
                jsonb_build_object(
                    'grantee', case
                        when acl.grantee = 0 then 'PUBLIC'
                        else pg_catalog.pg_get_userbyid(acl.grantee)
                    end,
                    'grantor', pg_catalog.pg_get_userbyid(acl.grantor),
                    'privilege', lower(acl.privilege_type),
                    'grantable', acl.is_grantable
                )
                order by
                    case
                        when acl.grantee = 0 then 'PUBLIC'
                        else pg_catalog.pg_get_userbyid(acl.grantee)
                    end,
                    lower(acl.privilege_type),
                    pg_catalog.pg_get_userbyid(acl.grantor),
                    acl.is_grantable
            )
            from pg_catalog.aclexplode(
                coalesce(
                    namespace.nspacl,
                    pg_catalog.acldefault('n', namespace.nspowner)
                )
            ) acl
        ),
        '[]'::jsonb
    ) as acl,
    coalesce(
        (
            select jsonb_agg(
                jsonb_build_object(
                    'provider', security_label.provider,
                    'label', security_label.label
                )
                order by security_label.provider, security_label.label
            )
            from pg_catalog.pg_seclabel security_label
            where security_label.classoid = 'pg_namespace'::regclass
              and security_label.objoid = namespace.oid
              and security_label.objsubid = 0
        ),
        '[]'::jsonb
    ) as security_labels,
    obj_description(namespace.oid, 'pg_namespace') as comment
from pg_catalog.pg_namespace namespace
where namespace.nspname = any(%(schemas)s)
order by namespace.nspname;
"""


_RELATION_SQL = """
/* schema-control:relations */
select
    namespace.nspname as schema_name,
    relation.relname as relation_name,
    case relation.relkind
        when 'r' then 'table'
        when 'p' then 'partitioned_table'
        when 'v' then 'view'
        when 'm' then 'materialized_view'
        when 'S' then 'sequence'
        when 'f' then 'foreign_table'
    end as relation_kind,
    case relation.relpersistence
        when 'p' then 'permanent'
        when 'u' then 'unlogged'
        when 't' then 'temporary'
    end as persistence,
    relation.relispartition as is_partition,
    relation.relrowsecurity as rls_enabled,
    relation.relforcerowsecurity as rls_forced,
    pg_catalog.pg_get_userbyid(relation.relowner) as owner,
    coalesce(
        (
            select jsonb_agg(
                jsonb_build_object(
                    'grantee', case
                        when acl.grantee = 0 then 'PUBLIC'
                        else pg_catalog.pg_get_userbyid(acl.grantee)
                    end,
                    'grantor', pg_catalog.pg_get_userbyid(acl.grantor),
                    'privilege', lower(acl.privilege_type),
                    'grantable', acl.is_grantable
                )
                order by
                    case
                        when acl.grantee = 0 then 'PUBLIC'
                        else pg_catalog.pg_get_userbyid(acl.grantee)
                    end,
                    lower(acl.privilege_type),
                    pg_catalog.pg_get_userbyid(acl.grantor),
                    acl.is_grantable
            )
            from pg_catalog.aclexplode(
                coalesce(
                    relation.relacl,
                    case relation.relkind
                        when 'S' then pg_catalog.acldefault('s', relation.relowner)
                        else pg_catalog.acldefault('r', relation.relowner)
                    end
                )
            ) acl
        ),
        '[]'::jsonb
    ) as acl,
    coalesce(
        (
            select jsonb_agg(
                jsonb_build_object(
                    'provider', security_label.provider,
                    'label', security_label.label
                )
                order by security_label.provider, security_label.label
            )
            from pg_catalog.pg_seclabel security_label
            where security_label.classoid = 'pg_class'::regclass
              and security_label.objoid = relation.oid
              and security_label.objsubid = 0
        ),
        '[]'::jsonb
    ) as security_labels,
    coalesce(
        array(
            select relation_option.value
            from unnest(
                coalesce(relation.reloptions, array[]::text[])
            ) relation_option(value)
            order by relation_option.value
        ),
        array[]::text[]
    ) as relation_options,
    case relation.relreplident
        when 'd' then 'default'
        when 'n' then 'nothing'
        when 'f' then 'full'
        when 'i' then 'index'
    end as replica_identity,
    case
        when relation.relkind in ('v', 'm')
        then pg_catalog.pg_get_viewdef(relation.oid, true)
    end as definition,
    foreign_server.srvname as foreign_server,
    foreign_table.ftoptions as foreign_options,
    pg_catalog.format_type(sequence_metadata.seqtypid, null) as sequence_data_type,
    sequence_metadata.seqstart as sequence_start,
    sequence_metadata.seqincrement as sequence_increment,
    sequence_metadata.seqmin as sequence_minimum,
    sequence_metadata.seqmax as sequence_maximum,
    sequence_metadata.seqcache as sequence_cache,
    sequence_metadata.seqcycle as sequence_cycle,
    obj_description(relation.oid, 'pg_class') as comment
from pg_catalog.pg_class relation
join pg_catalog.pg_namespace namespace on namespace.oid = relation.relnamespace
left join pg_catalog.pg_foreign_table foreign_table
    on foreign_table.ftrelid = relation.oid
left join pg_catalog.pg_foreign_server foreign_server
    on foreign_server.oid = foreign_table.ftserver
left join pg_catalog.pg_sequence sequence_metadata
    on sequence_metadata.seqrelid = relation.oid
where namespace.nspname = any(%(schemas)s)
  and relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f')
order by namespace.nspname, relation.relname;
"""


_COLUMN_SQL = """
/* schema-control:columns */
select
    namespace.nspname as schema_name,
    relation.relname as relation_name,
    attribute.attnum as ordinal_position,
    attribute.attname as column_name,
    pg_catalog.format_type(attribute.atttypid, attribute.atttypmod) as data_type,
    type_namespace.nspname as type_schema,
    data_type.typname as type_name,
    not attribute.attnotnull as nullable,
    case
        when attribute.attgenerated = ''
        then pg_catalog.pg_get_expr(default_value.adbin, default_value.adrelid, true)
    end as default_expression,
    case
        when attribute.attidentity = 'a' then 'always'
        when attribute.attidentity = 'd' then 'by_default'
    end as identity_generation,
    case
        when attribute.attgenerated = 's' then 'stored'
        when attribute.attgenerated <> '' then attribute.attgenerated::text
    end as generated_kind,
    case
        when attribute.attgenerated <> ''
        then pg_catalog.pg_get_expr(default_value.adbin, default_value.adrelid, true)
    end as generated_expression,
    case
        when attribute.attcollation = 0 then null
        else collation_namespace.nspname || '.' || collation.collname
    end as collation,
    coalesce(
        (
            select jsonb_agg(
                jsonb_build_object(
                    'provider', security_label.provider,
                    'label', security_label.label
                )
                order by security_label.provider, security_label.label
            )
            from pg_catalog.pg_seclabel security_label
            where security_label.classoid = 'pg_class'::regclass
              and security_label.objoid = relation.oid
              and security_label.objsubid = attribute.attnum
        ),
        '[]'::jsonb
    ) as security_labels,
    pg_catalog.col_description(relation.oid, attribute.attnum) as comment
from pg_catalog.pg_attribute attribute
join pg_catalog.pg_class relation on relation.oid = attribute.attrelid
join pg_catalog.pg_namespace namespace on namespace.oid = relation.relnamespace
join pg_catalog.pg_type data_type on data_type.oid = attribute.atttypid
join pg_catalog.pg_namespace type_namespace on type_namespace.oid = data_type.typnamespace
left join pg_catalog.pg_attrdef default_value
    on default_value.adrelid = attribute.attrelid
   and default_value.adnum = attribute.attnum
left join pg_catalog.pg_collation collation on collation.oid = attribute.attcollation
left join pg_catalog.pg_namespace collation_namespace
    on collation_namespace.oid = collation.collnamespace
where namespace.nspname = any(%(schemas)s)
  and relation.relkind in ('r', 'p', 'v', 'm', 'f')
  and attribute.attnum > 0
  and not attribute.attisdropped
order by namespace.nspname, relation.relname, attribute.attnum;
"""


_CONSTRAINT_SQL = """
/* schema-control:constraints */
select
    namespace.nspname as schema_name,
    relation.relname as relation_name,
    constraint_record.conname as constraint_name,
    case constraint_record.contype
        when 'p' then 'primary_key'
        when 'f' then 'foreign_key'
        when 'u' then 'unique'
        when 'c' then 'check'
        when 'x' then 'exclusion'
    end as constraint_type,
    coalesce(
        (
            select array_agg(source_attribute.attname order by source_key.ordinality)
            from unnest(constraint_record.conkey) with ordinality
                as source_key(attribute_number, ordinality)
            join pg_catalog.pg_attribute source_attribute
              on source_attribute.attrelid = constraint_record.conrelid
             and source_attribute.attnum = source_key.attribute_number
        ),
        array[]::text[]
    ) as source_columns,
    target_namespace.nspname as referenced_schema,
    target_relation.relname as referenced_table,
    coalesce(
        (
            select array_agg(target_attribute.attname order by target_key.ordinality)
            from unnest(constraint_record.confkey) with ordinality
                as target_key(attribute_number, ordinality)
            join pg_catalog.pg_attribute target_attribute
              on target_attribute.attrelid = constraint_record.confrelid
             and target_attribute.attnum = target_key.attribute_number
        ),
        array[]::text[]
    ) as referenced_columns,
    pg_catalog.pg_get_constraintdef(constraint_record.oid, true) as definition,
    constraint_record.condeferrable as deferrable,
    constraint_record.condeferred as initially_deferred,
    constraint_record.convalidated as validated,
    constraint_record.connoinherit as no_inherit,
    case constraint_record.confmatchtype
        when 'f' then 'full'
        when 'p' then 'partial'
        when 's' then 'simple'
    end as match_type,
    case constraint_record.confupdtype
        when 'a' then 'no_action'
        when 'r' then 'restrict'
        when 'c' then 'cascade'
        when 'n' then 'set_null'
        when 'd' then 'set_default'
    end as update_action,
    case constraint_record.confdeltype
        when 'a' then 'no_action'
        when 'r' then 'restrict'
        when 'c' then 'cascade'
        when 'n' then 'set_null'
        when 'd' then 'set_default'
    end as delete_action,
    obj_description(constraint_record.oid, 'pg_constraint') as comment
from pg_catalog.pg_constraint constraint_record
join pg_catalog.pg_class relation on relation.oid = constraint_record.conrelid
join pg_catalog.pg_namespace namespace on namespace.oid = relation.relnamespace
left join pg_catalog.pg_class target_relation
    on target_relation.oid = constraint_record.confrelid
left join pg_catalog.pg_namespace target_namespace
    on target_namespace.oid = target_relation.relnamespace
where namespace.nspname = any(%(schemas)s)
  and constraint_record.contype in ('p', 'f', 'u', 'c', 'x')
order by namespace.nspname, relation.relname, constraint_record.conname;
"""


_INDEX_SQL = """
/* schema-control:indexes */
select
    namespace.nspname as schema_name,
    relation.relname as relation_name,
    index_relation.relname as index_name,
    access_method.amname as method,
    index_record.indisunique as is_unique,
    index_record.indisprimary as is_primary,
    index_record.indisvalid as valid,
    index_record.indisready as ready,
    index_record.indislive as live,
    index_record.indisreplident as is_replica_identity,
    pg_catalog.pg_get_userbyid(index_relation.relowner) as owner,
    coalesce(
        array(
            select index_option.value
            from unnest(
                coalesce(index_relation.reloptions, array[]::text[])
            ) index_option(value)
            order by index_option.value
        ),
        array[]::text[]
    ) as index_options,
    coalesce(
        (
            select jsonb_agg(
                jsonb_build_object(
                    'provider', security_label.provider,
                    'label', security_label.label
                )
                order by security_label.provider, security_label.label
            )
            from pg_catalog.pg_seclabel security_label
            where security_label.classoid = 'pg_class'::regclass
              and security_label.objoid = index_relation.oid
              and security_label.objsubid = 0
        ),
        '[]'::jsonb
    ) as security_labels,
    coalesce(
        (
            select array_agg(
                pg_catalog.pg_get_indexdef(index_record.indexrelid, position, true)
                order by position
            )
            from generate_series(1, index_record.indnkeyatts) position
        ),
        array[]::text[]
    ) as key_columns,
    coalesce(
        (
            select array_agg(
                pg_catalog.pg_get_indexdef(index_record.indexrelid, position, true)
                order by position
            )
            from generate_series(index_record.indnkeyatts + 1, index_record.indnatts) position
        ),
        array[]::text[]
    ) as included_columns,
    pg_catalog.pg_get_expr(index_record.indpred, index_record.indrelid, true) as predicate,
    pg_catalog.pg_get_expr(index_record.indexprs, index_record.indrelid, true) as expression,
    pg_catalog.pg_get_indexdef(index_record.indexrelid) as definition,
    obj_description(index_relation.oid, 'pg_class') as comment
from pg_catalog.pg_index index_record
join pg_catalog.pg_class relation on relation.oid = index_record.indrelid
join pg_catalog.pg_namespace namespace on namespace.oid = relation.relnamespace
join pg_catalog.pg_class index_relation on index_relation.oid = index_record.indexrelid
join pg_catalog.pg_am access_method on access_method.oid = index_relation.relam
where namespace.nspname = any(%(schemas)s)
order by namespace.nspname, relation.relname, index_relation.relname;
"""


_ROUTINE_SQL = """
/* schema-control:routines */
select
    namespace.nspname as schema_name,
    routine.proname as routine_name,
    routine.proname || '(' || pg_catalog.pg_get_function_identity_arguments(routine.oid) || ')'
        as signature,
    case routine.prokind
        when 'p' then 'procedure'
        when 'w' then 'window_function'
        else 'function'
    end as routine_kind,
    pg_catalog.pg_get_function_result(routine.oid) as result_type,
    language.lanname as language,
    case routine.provolatile
        when 'i' then 'immutable'
        when 's' then 'stable'
        when 'v' then 'volatile'
    end as volatility,
    case routine.proparallel
        when 's' then 'safe'
        when 'r' then 'restricted'
        when 'u' then 'unsafe'
    end as parallel_safety,
    routine.prosecdef as security_definer,
    routine.proisstrict as strict,
    pg_catalog.pg_get_userbyid(routine.proowner) as owner,
    coalesce(
        (
            select jsonb_agg(
                jsonb_build_object(
                    'grantee', case
                        when acl.grantee = 0 then 'PUBLIC'
                        else pg_catalog.pg_get_userbyid(acl.grantee)
                    end,
                    'grantor', pg_catalog.pg_get_userbyid(acl.grantor),
                    'privilege', lower(acl.privilege_type),
                    'grantable', acl.is_grantable
                )
                order by
                    case
                        when acl.grantee = 0 then 'PUBLIC'
                        else pg_catalog.pg_get_userbyid(acl.grantee)
                    end,
                    lower(acl.privilege_type),
                    pg_catalog.pg_get_userbyid(acl.grantor),
                    acl.is_grantable
            )
            from pg_catalog.aclexplode(
                coalesce(
                    routine.proacl,
                    pg_catalog.acldefault('f', routine.proowner)
                )
            ) acl
        ),
        '[]'::jsonb
    ) as acl,
    coalesce(
        (
            select jsonb_agg(
                jsonb_build_object(
                    'provider', security_label.provider,
                    'label', security_label.label
                )
                order by security_label.provider, security_label.label
            )
            from pg_catalog.pg_seclabel security_label
            where security_label.classoid = 'pg_proc'::regclass
              and security_label.objoid = routine.oid
              and security_label.objsubid = 0
        ),
        '[]'::jsonb
    ) as security_labels,
    pg_catalog.pg_get_functiondef(routine.oid) as definition,
    obj_description(routine.oid, 'pg_proc') as comment
from pg_catalog.pg_proc routine
join pg_catalog.pg_namespace namespace on namespace.oid = routine.pronamespace
join pg_catalog.pg_language language on language.oid = routine.prolang
where namespace.nspname = any(%(schemas)s)
  and routine.prokind in ('f', 'p', 'w')
order by namespace.nspname, signature;
"""


_TRIGGER_SQL = """
/* schema-control:triggers */
select
    namespace.nspname as schema_name,
    relation.relname as relation_name,
    trigger_record.tgname as trigger_name,
    case
        when (trigger_record.tgtype & 64) <> 0 then 'instead_of'
        when (trigger_record.tgtype & 2) <> 0 then 'before'
        else 'after'
    end as timing,
    array_remove(
        array[
            case when (trigger_record.tgtype & 4) <> 0 then 'insert'::text end,
            case when (trigger_record.tgtype & 8) <> 0 then 'delete'::text end,
            case when (trigger_record.tgtype & 16) <> 0 then 'update'::text end,
            case when (trigger_record.tgtype & 32) <> 0 then 'truncate'::text end
        ],
        null
    ) as events,
    case when (trigger_record.tgtype & 1) <> 0 then 'row' else 'statement' end as level,
    case trigger_record.tgenabled
        when 'O' then 'origin'
        when 'D' then 'disabled'
        when 'R' then 'replica'
        when 'A' then 'always'
    end as enabled,
    function_namespace.nspname as function_schema,
    trigger_function.proname || '(' ||
        pg_catalog.pg_get_function_identity_arguments(trigger_function.oid) || ')'
        as function_signature,
    pg_catalog.pg_get_triggerdef(trigger_record.oid, true) as definition,
    obj_description(trigger_record.oid, 'pg_trigger') as comment
from pg_catalog.pg_trigger trigger_record
join pg_catalog.pg_class relation on relation.oid = trigger_record.tgrelid
join pg_catalog.pg_namespace namespace on namespace.oid = relation.relnamespace
join pg_catalog.pg_proc trigger_function on trigger_function.oid = trigger_record.tgfoid
join pg_catalog.pg_namespace function_namespace
    on function_namespace.oid = trigger_function.pronamespace
where namespace.nspname = any(%(schemas)s)
  and not trigger_record.tgisinternal
order by namespace.nspname, relation.relname, trigger_record.tgname;
"""


_ENUM_SQL = """
/* schema-control:enums */
select
    namespace.nspname as schema_name,
    data_type.typname as type_name,
    pg_catalog.pg_get_userbyid(data_type.typowner) as owner,
    array_agg(enum_value.enumlabel order by enum_value.enumsortorder) as labels,
    coalesce(
        (
            select jsonb_agg(
                jsonb_build_object(
                    'provider', security_label.provider,
                    'label', security_label.label
                )
                order by security_label.provider, security_label.label
            )
            from pg_catalog.pg_seclabel security_label
            where security_label.classoid = 'pg_type'::regclass
              and security_label.objoid = data_type.oid
              and security_label.objsubid = 0
        ),
        '[]'::jsonb
    ) as security_labels,
    obj_description(data_type.oid, 'pg_type') as comment
from pg_catalog.pg_type data_type
join pg_catalog.pg_namespace namespace on namespace.oid = data_type.typnamespace
join pg_catalog.pg_enum enum_value on enum_value.enumtypid = data_type.oid
where namespace.nspname = any(%(schemas)s)
group by namespace.nspname, data_type.oid, data_type.typname, data_type.typowner
order by namespace.nspname, data_type.typname;
"""


_DOMAIN_SQL = """
/* schema-control:domains */
select
    namespace.nspname as schema_name,
    domain_type.typname as type_name,
    pg_catalog.pg_get_userbyid(domain_type.typowner) as owner,
    pg_catalog.format_type(domain_type.typbasetype, domain_type.typtypmod) as base_type,
    domain_type.typnotnull as not_null,
    domain_type.typdefault as default_expression,
    coalesce(
        (
            select array_agg(
                pg_catalog.pg_get_constraintdef(domain_constraint.oid, true)
                order by domain_constraint.conname
            )
            from pg_catalog.pg_constraint domain_constraint
            where domain_constraint.contypid = domain_type.oid
        ),
        array[]::text[]
    ) as constraints,
    case
        when domain_type.typcollation = 0 then null
        else collation_namespace.nspname || '.' || collation.collname
    end as collation,
    coalesce(
        (
            select jsonb_agg(
                jsonb_build_object(
                    'provider', security_label.provider,
                    'label', security_label.label
                )
                order by security_label.provider, security_label.label
            )
            from pg_catalog.pg_seclabel security_label
            where security_label.classoid = 'pg_type'::regclass
              and security_label.objoid = domain_type.oid
              and security_label.objsubid = 0
        ),
        '[]'::jsonb
    ) as security_labels,
    obj_description(domain_type.oid, 'pg_type') as comment
from pg_catalog.pg_type domain_type
join pg_catalog.pg_namespace namespace on namespace.oid = domain_type.typnamespace
left join pg_catalog.pg_collation collation on collation.oid = domain_type.typcollation
left join pg_catalog.pg_namespace collation_namespace
    on collation_namespace.oid = collation.collnamespace
where namespace.nspname = any(%(schemas)s)
  and domain_type.typtype = 'd'
order by namespace.nspname, domain_type.typname;
"""


_COMPOSITE_SQL = """
/* schema-control:composites */
select
    namespace.nspname as schema_name,
    composite_type.typname as type_name,
    pg_catalog.pg_get_userbyid(composite_type.typowner) as owner,
    attribute.attnum as ordinal_position,
    attribute.attname as attribute_name,
    pg_catalog.format_type(attribute.atttypid, attribute.atttypmod) as data_type,
    not attribute.attnotnull as nullable,
    pg_catalog.col_description(composite_relation.oid, attribute.attnum) as attribute_comment,
    coalesce(
        (
            select jsonb_agg(
                jsonb_build_object(
                    'provider', security_label.provider,
                    'label', security_label.label
                )
                order by security_label.provider, security_label.label
            )
            from pg_catalog.pg_seclabel security_label
            where security_label.classoid = 'pg_type'::regclass
              and security_label.objoid = composite_type.oid
              and security_label.objsubid = 0
        ),
        '[]'::jsonb
    ) as security_labels,
    obj_description(composite_type.oid, 'pg_type') as comment
from pg_catalog.pg_type composite_type
join pg_catalog.pg_namespace namespace on namespace.oid = composite_type.typnamespace
join pg_catalog.pg_class composite_relation on composite_relation.oid = composite_type.typrelid
join pg_catalog.pg_attribute attribute on attribute.attrelid = composite_relation.oid
where namespace.nspname = any(%(schemas)s)
  and composite_type.typtype = 'c'
  and composite_relation.relkind = 'c'
  and attribute.attnum > 0
  and not attribute.attisdropped
order by namespace.nspname, composite_type.typname, attribute.attnum;
"""


_POLICY_SQL = """
/* schema-control:policies */
select
    namespace.nspname as schema_name,
    relation.relname as relation_name,
    policy.polname as policy_name,
    policy.polpermissive as permissive,
    case policy.polcmd
        when '*' then 'all'
        when 'r' then 'select'
        when 'a' then 'insert'
        when 'w' then 'update'
        when 'd' then 'delete'
    end as command,
    pg_catalog.pg_get_expr(policy.polqual, policy.polrelid, true) as using_expression,
    pg_catalog.pg_get_expr(policy.polwithcheck, policy.polrelid, true) as check_expression,
    coalesce(
        array(
            select case
                when target_role.role_oid = 0 then 'PUBLIC'
                else pg_catalog.pg_get_userbyid(target_role.role_oid)
            end
            from unnest(policy.polroles) target_role(role_oid)
            order by case
                when target_role.role_oid = 0 then 'PUBLIC'
                else pg_catalog.pg_get_userbyid(target_role.role_oid)
            end
        ),
        array[]::text[]
    ) as target_roles
from pg_catalog.pg_policy policy
join pg_catalog.pg_class relation on relation.oid = policy.polrelid
join pg_catalog.pg_namespace namespace on namespace.oid = relation.relnamespace
where namespace.nspname = any(%(schemas)s)
order by namespace.nspname, relation.relname, policy.polname;
"""


_PARTITION_SQL = """
/* schema-control:partitions */
select
    child_namespace.nspname as schema_name,
    child.relname as partition_name,
    parent_namespace.nspname as parent_schema,
    parent.relname as parent_name,
    case partitioned.partstrat
        when 'r' then 'range'
        when 'l' then 'list'
        when 'h' then 'hash'
    end as strategy,
    pg_catalog.pg_get_partkeydef(parent.oid) as key_definition,
    pg_catalog.pg_get_expr(child.relpartbound, child.oid, true) as bound
from pg_catalog.pg_inherits inheritance
join pg_catalog.pg_class child on child.oid = inheritance.inhrelid
join pg_catalog.pg_namespace child_namespace on child_namespace.oid = child.relnamespace
join pg_catalog.pg_class parent on parent.oid = inheritance.inhparent
join pg_catalog.pg_namespace parent_namespace on parent_namespace.oid = parent.relnamespace
left join pg_catalog.pg_partitioned_table partitioned on partitioned.partrelid = parent.oid
where child_namespace.nspname = any(%(schemas)s)
  and child.relispartition
order by child_namespace.nspname, child.relname, parent_namespace.nspname, parent.relname;
"""


_EXTENSION_SQL = """
/* schema-control:extensions */
select
    namespace.nspname as schema_name,
    extension.extname as extension_name,
    pg_catalog.pg_get_userbyid(extension.extowner) as owner,
    extension.extversion as version,
    extension.extrelocatable as relocatable,
    coalesce(
        (
            select jsonb_agg(
                jsonb_build_object(
                    'provider', security_label.provider,
                    'label', security_label.label
                )
                order by security_label.provider, security_label.label
            )
            from pg_catalog.pg_seclabel security_label
            where security_label.classoid = 'pg_extension'::regclass
              and security_label.objoid = extension.oid
              and security_label.objsubid = 0
        ),
        '[]'::jsonb
    ) as security_labels,
    obj_description(extension.oid, 'pg_extension') as comment
from pg_catalog.pg_extension extension
join pg_catalog.pg_namespace namespace on namespace.oid = extension.extnamespace
where namespace.nspname = any(%(schemas)s)
order by namespace.nspname, extension.extname;
"""


_DEPENDENCY_SQL = """
/* schema-control:dependencies */
with normal_dependencies as (
    select
        source_object.type_name as source_kind,
        source_object.schema_name as source_schema,
        source_object.object_name as source_name,
        source_object.identity as source_identity,
        target_object.type_name as target_kind,
        target_object.schema_name as target_schema,
        target_object.object_name as target_name,
        target_object.identity as target_identity,
        'normal'::text as dependency_kind
    from pg_catalog.pg_depend dependency
    cross join lateral pg_catalog.pg_identify_object(
        dependency.classid,
        dependency.objid,
        dependency.objsubid
    ) as source_object(type_name, schema_name, object_name, identity)
    cross join lateral pg_catalog.pg_identify_object(
        dependency.refclassid,
        dependency.refobjid,
        dependency.refobjsubid
    ) as target_object(type_name, schema_name, object_name, identity)
    where dependency.deptype = 'n'
      and source_object.schema_name = any(%(schemas)s)
      and target_object.schema_name = any(%(schemas)s)
), view_dependencies as (
    select
        case view_relation.relkind
            when 'v' then 'view'
            when 'm' then 'materialized view'
        end as source_kind,
        view_namespace.nspname as source_schema,
        view_relation.relname as source_name,
        view_namespace.nspname || '.' || view_relation.relname as source_identity,
        'relation'::text as target_kind,
        target_namespace.nspname as target_schema,
        target_relation.relname as target_name,
        target_namespace.nspname || '.' || target_relation.relname as target_identity,
        'view_reference'::text as dependency_kind
    from pg_catalog.pg_rewrite rewrite_rule
    join pg_catalog.pg_class view_relation on view_relation.oid = rewrite_rule.ev_class
    join pg_catalog.pg_namespace view_namespace on view_namespace.oid = view_relation.relnamespace
    join pg_catalog.pg_depend dependency
      on dependency.classid = 'pg_rewrite'::regclass
     and dependency.objid = rewrite_rule.oid
     and dependency.refclassid = 'pg_class'::regclass
    join pg_catalog.pg_class target_relation on target_relation.oid = dependency.refobjid
    join pg_catalog.pg_namespace target_namespace on target_namespace.oid = target_relation.relnamespace
    where view_relation.relkind in ('v', 'm')
      and view_namespace.nspname = any(%(schemas)s)
      and target_namespace.nspname = any(%(schemas)s)
      and view_relation.oid <> target_relation.oid
)
select distinct
    source_kind,
    source_schema,
    source_name,
    source_identity,
    target_kind,
    target_schema,
    target_name,
    target_identity,
    dependency_kind
from (
    select * from normal_dependencies
    union all
    select * from view_dependencies
) dependencies
order by
    source_schema,
    source_identity,
    target_schema,
    target_identity,
    dependency_kind;
"""


_QUERIES: tuple[tuple[str, str], ...] = (
    ("schemas", _SCHEMA_SQL),
    ("relations", _RELATION_SQL),
    ("columns", _COLUMN_SQL),
    ("constraints", _CONSTRAINT_SQL),
    ("indexes", _INDEX_SQL),
    ("routines", _ROUTINE_SQL),
    ("triggers", _TRIGGER_SQL),
    ("enums", _ENUM_SQL),
    ("domains", _DOMAIN_SQL),
    ("composites", _COMPOSITE_SQL),
    ("policies", _POLICY_SQL),
    ("partitions", _PARTITION_SQL),
    ("extensions", _EXTENSION_SQL),
    ("dependencies", _DEPENDENCY_SQL),
)


_SNAPSHOT_SETUP_SQL: tuple[str, ...] = (
    """/* schema-control:snapshot */
set transaction isolation level repeatable read, read only;
""",
    """/* schema-control:searchpath */
set local search_path = pg_catalog;
""",
    """/* schema-control:timezone */
set local time zone 'UTC';
""",
)


def extract_catalog(
    database_url: str,
    config: dict,
    connection_factory: ConnectionFactory | None = None,
) -> dict:
    """Extract a deterministic manifest for configured physical schemas.

    ``config['migration_sets']`` supplies logical module ids and physical schema
    names.  Multiple modules may intentionally share a schema (Direct Lending
    and Security Master do so by default), therefore both identities are kept.
    Optional ``audit_schemas`` are inspected and emitted without being presented
    as migration-owned physical schemas. An injected ``connection_factory`` must
    accept ``database_url`` and return an object with ``cursor()``; cursor rows
    must be mappings.
    """

    if not isinstance(database_url, str) or not database_url.strip():
        raise ValueError("database_url cannot be empty.")
    if not isinstance(config, Mapping):
        raise TypeError("config must be a mapping.")

    module_mapping = _module_schema_mapping(config)
    physical_schemas = sorted({item["physical_schema"] for item in module_mapping})
    inspected_schemas = sorted(set(physical_schemas) | set(_audit_schemas(config)))
    connection = _connect(database_url, connection_factory)
    cursor = None
    try:
        cursor = connection.cursor()
        with _catalog_snapshot(connection, cursor):
            rows = {
                name: _fetch_rows(cursor, query, inspected_schemas)
                for name, query in _QUERIES
            }
    finally:
        if cursor is not None and hasattr(cursor, "close"):
            cursor.close()
        if hasattr(connection, "close"):
            connection.close()

    return _build_manifest(
        config,
        physical_schemas,
        inspected_schemas,
        module_mapping,
        rows,
    )


def _connect(database_url: str, connection_factory: ConnectionFactory | None) -> Any:
    if connection_factory is not None:
        return connection_factory(database_url)
    try:
        import psycopg  # type: ignore[import-not-found]
        from psycopg.rows import dict_row  # type: ignore[import-not-found]
    except ImportError as exc:
        raise RuntimeError(
            "PostgreSQL catalog extraction requires optional dependency 'psycopg'. "
            "Install tools/schema_control/requirements.txt or inject connection_factory."
        ) from exc
    return psycopg.connect(database_url, row_factory=dict_row)


@contextmanager
def _catalog_snapshot(connection: Any, cursor: Any) -> Any:
    """Run extraction in one stable, read-only PostgreSQL snapshot.

    Real psycopg connections expose ``transaction()``. Small injected fakes may
    omit it, so the context is optional while the session-setting statements
    remain observable and testable through the cursor seam.
    """

    transaction_factory = getattr(connection, "transaction", None)
    if callable(transaction_factory):
        with transaction_factory():
            _configure_snapshot(cursor)
            yield
        return

    _configure_snapshot(cursor)
    yield


def _configure_snapshot(cursor: Any) -> None:
    for statement in _SNAPSHOT_SETUP_SQL:
        try:
            cursor.execute(statement)
        except TypeError as exc:
            # Legacy and intentionally tiny test fakes often require a params
            # argument even for SQL without placeholders. Psycopg does not.
            try:
                cursor.execute(statement, {})
            except TypeError:
                raise exc


def _fetch_rows(cursor: Any, query: str, schemas: list[str]) -> list[dict[str, Any]]:
    cursor.execute(query, {"schemas": schemas})
    fetched = cursor.fetchall()
    result: list[dict[str, Any]] = []
    for row in fetched:
        if not isinstance(row, Mapping):
            raise TypeError(
                "Catalog cursors must return mapping rows; configure psycopg with dict_row."
            )
        result.append(_json_ready(row))
    return result


def _module_schema_mapping(config: Mapping[str, Any]) -> list[dict[str, Any]]:
    raw_sets = config.get("migration_sets", config.get("migrationSets"))
    if raw_sets is None and isinstance(config.get("schema_control"), Mapping):
        nested = config["schema_control"]
        raw_sets = nested.get("migration_sets", nested.get("migrationSets"))
    if isinstance(raw_sets, Mapping):
        entries: list[Mapping[str, Any]] = []
        for key, value in raw_sets.items():
            if not isinstance(value, Mapping):
                raise TypeError("Each configured migration set must be a mapping.")
            entries.append({"id": key, **value})
    elif isinstance(raw_sets, Sequence) and not isinstance(
        raw_sets, (str, bytes, bytearray)
    ):
        entries = []
        for value in raw_sets:
            if not isinstance(value, Mapping):
                raise TypeError("Each configured migration set must be a mapping.")
            entries.append(value)
    else:
        raise ValueError("config must define a non-empty migration_sets collection.")
    if not entries:
        raise ValueError("config must define a non-empty migration_sets collection.")

    result: list[dict[str, Any]] = []
    seen_modules: set[str] = set()
    for entry in entries:
        module_id = _first_text(entry, "id", "module", "name")
        schema = _first_text(
            entry,
            "schema",
            "physical_schema",
            "default_schema",
            "schema_name",
        )
        if not module_id:
            raise ValueError("Every migration set must define a non-empty id.")
        if module_id in seen_modules:
            raise ValueError(
                f"Migration set id '{module_id}' is configured more than once."
            )
        if not schema:
            raise ValueError(
                f"Migration set '{module_id}' must define a physical schema."
            )
        if _IDENTIFIER.fullmatch(schema) is None:
            raise ValueError(
                f"Migration set '{module_id}' uses unsupported PostgreSQL schema '{schema}'."
            )
        seen_modules.add(module_id)
        result.append(
            {
                "id": module_id,
                "display_name": _first_text(entry, "display_name", "displayName")
                or module_id,
                "physical_schema": schema,
            }
        )
    return sorted(result, key=lambda item: (item["id"], item["physical_schema"]))


def _audit_schemas(config: Mapping[str, Any]) -> list[str]:
    raw_schemas = config.get("audit_schemas", [])
    if raw_schemas is None:
        return []
    if not isinstance(raw_schemas, Sequence) or isinstance(
        raw_schemas,
        (str, bytes, bytearray),
    ):
        raise TypeError("audit_schemas must be an array of PostgreSQL schema names.")

    result: set[str] = set()
    for value in raw_schemas:
        schema = normalize_text(str(value).strip())
        if not schema:
            raise ValueError("audit_schemas cannot contain an empty schema name.")
        if _IDENTIFIER.fullmatch(schema) is None:
            raise ValueError(f"Unsupported PostgreSQL audit schema '{schema}'.")
        result.add(schema)
    return sorted(result)


def _first_text(value: Mapping[str, Any], *keys: str) -> str:
    for key in keys:
        candidate = value.get(key)
        if candidate is not None and str(candidate).strip():
            return normalize_text(str(candidate).strip())
    return ""


def _json_ready(value: Any) -> Any:
    if isinstance(value, Mapping):
        return normalize_value(
            {str(key): _json_ready(item) for key, item in value.items()}
        )
    if isinstance(value, (list, tuple)):
        return [_json_ready(item) for item in value]
    if isinstance(value, (set, frozenset)):
        normalized = [_json_ready(item) for item in value]
        return sorted(normalized, key=lambda item: fingerprint(item))
    if isinstance(value, Decimal):
        return format(value, "f")
    if isinstance(value, (datetime, date, time)):
        return value.isoformat()
    if isinstance(value, UUID):
        return str(value)
    if isinstance(value, Path):
        return value.as_posix()
    if isinstance(value, memoryview):
        return value.tobytes().hex()
    if isinstance(value, (bytes, bytearray)):
        return bytes(value).hex()
    if isinstance(value, str):
        return normalize_text(value)
    return value


def _build_manifest(
    config: Mapping[str, Any],
    physical_schemas: list[str],
    inspected_schemas: list[str],
    module_mapping: list[dict[str, Any]],
    rows: Mapping[str, list[dict[str, Any]]],
) -> dict[str, Any]:
    schema_metadata = {
        str(row.get("schema_name")): row
        for row in rows["schemas"]
        if row.get("schema_name")
    }
    modules_by_schema: dict[str, list[str]] = defaultdict(list)
    for module in module_mapping:
        modules_by_schema[module["physical_schema"]].append(module["id"])

    relations = _build_relations(rows)
    routines = _build_routines(rows["routines"])
    enum_types = _named_objects(rows["enums"], "type_name", "name")
    domain_types = _named_objects(rows["domains"], "type_name", "name")
    composite_types = _build_composite_types(rows["composites"])
    partitions = _build_partitions(rows["partitions"])
    extensions = _named_objects(rows["extensions"], "extension_name", "name")
    dependencies = _build_dependencies(rows["dependencies"])

    object_fingerprints: dict[str, str] = {}
    schemas: list[dict[str, Any]] = []
    for schema_name in inspected_schemas:
        schema_relations = [
            relation for relation in relations if relation["schema"] == schema_name
        ]
        categories = {
            "tables": [
                relation
                for relation in schema_relations
                if relation["kind"] in {"table", "partitioned_table"}
            ],
            "views": [
                relation for relation in schema_relations if relation["kind"] == "view"
            ],
            "materialized_views": [
                relation
                for relation in schema_relations
                if relation["kind"] == "materialized_view"
            ],
            "sequences": [
                relation
                for relation in schema_relations
                if relation["kind"] == "sequence"
            ],
            "foreign_tables": [
                relation
                for relation in schema_relations
                if relation["kind"] == "foreign_table"
            ],
        }

        schema_functions = [
            item
            for item in routines
            if item["schema"] == schema_name and item["kind"] != "procedure"
        ]
        schema_procedures = [
            item
            for item in routines
            if item["schema"] == schema_name and item["kind"] == "procedure"
        ]
        schema_enums = [item for item in enum_types if item["schema"] == schema_name]
        schema_domains = [
            item for item in domain_types if item["schema"] == schema_name
        ]
        schema_composites = [
            item for item in composite_types if item["schema"] == schema_name
        ]
        schema_partitions = [
            item for item in partitions if item["schema"] == schema_name
        ]
        schema_extensions = [
            item for item in extensions if item["schema"] == schema_name
        ]
        schema_dependencies = [
            item for item in dependencies if item["source_schema"] == schema_name
        ]
        rls_status = _build_rls_status(categories["tables"])

        comments = _collect_comments(
            schema_name,
            schema_metadata.get(schema_name, {}).get("comment"),
            schema_relations,
            schema_functions + schema_procedures,
            schema_enums + schema_domains + schema_composites,
            schema_extensions,
        )
        schema_entry: dict[str, Any] = {
            "name": schema_name,
            "exists": schema_name in schema_metadata,
            "logical_modules": sorted(modules_by_schema[schema_name]),
            "owner": schema_metadata.get(schema_name, {}).get("owner"),
            "acl": _acl_entries(schema_metadata.get(schema_name, {}).get("acl")),
            "security_labels": _security_labels(
                schema_metadata.get(schema_name, {}).get("security_labels")
            ),
            "comment": schema_metadata.get(schema_name, {}).get("comment"),
            "relations": schema_relations,
            **categories,
            "functions": schema_functions,
            "procedures": schema_procedures,
            "triggers": sorted(
                [
                    trigger
                    for relation in schema_relations
                    for trigger in relation["triggers"]
                ],
                key=lambda item: (item["relation"], item["name"]),
            ),
            "enums": schema_enums,
            "domains": schema_domains,
            "composite_types": schema_composites,
            "row_level_security": rls_status,
            "policies": sorted(
                [
                    policy
                    for relation in schema_relations
                    for policy in relation["policies"]
                ],
                key=lambda item: (item["relation"], item["name"]),
            ),
            "partitions": schema_partitions,
            "extensions": schema_extensions,
            "comments": comments,
            "dependencies": schema_dependencies,
        }
        _fingerprint_schema_objects(schema_entry, object_fingerprints)
        schema_entry["fingerprint"] = fingerprint(
            {key: value for key, value in schema_entry.items() if key != "fingerprint"}
        )
        object_fingerprints[f"schema:{schema_name}"] = schema_entry["fingerprint"]
        schemas.append(schema_entry)

    manifest_format = "meridian.schema-control.v1"
    manifest_config = config.get("manifest")
    if isinstance(manifest_config, Mapping) and manifest_config.get("format"):
        manifest_format = normalize_text(str(manifest_config["format"]))
    manifest: dict[str, Any] = {
        "format": manifest_format,
        "physical_schemas": physical_schemas,
        "inspected_schemas": inspected_schemas,
        "module_schema_mapping": module_mapping,
        "schemas": schemas,
        "dependencies": dependencies,
        "object_fingerprints": {
            key: object_fingerprints[key] for key in sorted(object_fingerprints)
        },
    }
    manifest["database_fingerprint"] = fingerprint(manifest)
    return manifest


def _build_relations(rows: Mapping[str, list[dict[str, Any]]]) -> list[dict[str, Any]]:
    relation_map: dict[tuple[str, str], dict[str, Any]] = {}
    for row in rows["relations"]:
        schema = str(row.get("schema_name") or "")
        name = str(row.get("relation_name") or "")
        if not schema or not name:
            continue
        kind = str(row.get("relation_kind") or "relation")
        definition = _clean_definition(row.get("definition"))
        sequence = None
        if kind == "sequence":
            sequence = {
                "data_type": row.get("sequence_data_type"),
                "start": row.get("sequence_start"),
                "increment": row.get("sequence_increment"),
                "minimum": row.get("sequence_minimum"),
                "maximum": row.get("sequence_maximum"),
                "cache": row.get("sequence_cache"),
                "cycle": bool(row.get("sequence_cycle", False)),
            }
        relation_map[(schema, name)] = {
            "schema": schema,
            "name": name,
            "kind": kind,
            "persistence": row.get("persistence"),
            "is_partition": bool(row.get("is_partition", False)),
            "rls_enabled": bool(row.get("rls_enabled", False)),
            "rls_forced": bool(row.get("rls_forced", False)),
            "owner": row.get("owner"),
            "acl": _acl_entries(row.get("acl")),
            "security_labels": _security_labels(row.get("security_labels")),
            "options": sorted(_string_list(row.get("relation_options"))),
            "replica_identity": row.get("replica_identity"),
            "definition": definition,
            "definition_hash": sha256_text(definition) if definition else None,
            "foreign_server": row.get("foreign_server"),
            "foreign_options": sorted(_string_list(row.get("foreign_options"))),
            "sequence": sequence,
            "comment": row.get("comment"),
            "columns": [],
            "constraints": [],
            "indexes": [],
            "triggers": [],
            "policies": [],
        }

    for row in rows["columns"]:
        relation = relation_map.get(_relation_key(row))
        if relation is None:
            continue
        relation["columns"].append(
            {
                "name": str(row.get("column_name") or ""),
                "ordinal_position": int(row.get("ordinal_position") or 0),
                "data_type": row.get("data_type"),
                "type_schema": row.get("type_schema"),
                "type_name": row.get("type_name"),
                "nullable": bool(row.get("nullable", False)),
                "default": _clean_definition(row.get("default_expression")),
                "identity": row.get("identity_generation"),
                "generated": row.get("generated_kind"),
                "generated_expression": _clean_definition(
                    row.get("generated_expression")
                ),
                "collation": row.get("collation"),
                "security_labels": _security_labels(row.get("security_labels")),
                "comment": row.get("comment"),
            }
        )

    for row in rows["constraints"]:
        relation = relation_map.get(_relation_key(row))
        if relation is None:
            continue
        relation["constraints"].append(
            {
                "name": str(row.get("constraint_name") or ""),
                "type": str(row.get("constraint_type") or ""),
                "columns": _string_list(row.get("source_columns")),
                "referenced_schema": row.get("referenced_schema"),
                "referenced_table": row.get("referenced_table"),
                "referenced_columns": _string_list(row.get("referenced_columns")),
                "definition": _clean_definition(row.get("definition")),
                "deferrable": bool(row.get("deferrable", False)),
                "initially_deferred": bool(row.get("initially_deferred", False)),
                "validated": bool(row.get("validated", False)),
                "no_inherit": bool(row.get("no_inherit", False)),
                "match_type": row.get("match_type"),
                "update_action": row.get("update_action"),
                "delete_action": row.get("delete_action"),
                "comment": row.get("comment"),
            }
        )

    for row in rows["indexes"]:
        relation = relation_map.get(_relation_key(row))
        if relation is None:
            continue
        relation["indexes"].append(
            {
                "name": str(row.get("index_name") or ""),
                "method": row.get("method"),
                "unique": bool(row.get("is_unique", False)),
                "primary": bool(row.get("is_primary", False)),
                "valid": bool(row.get("valid", False)),
                "ready": bool(row.get("ready", False)),
                "live": bool(row.get("live", False)),
                "is_replica_identity": bool(row.get("is_replica_identity", False)),
                "owner": row.get("owner"),
                "options": sorted(_string_list(row.get("index_options"))),
                "security_labels": _security_labels(row.get("security_labels")),
                "key_columns": _string_list(row.get("key_columns")),
                "included_columns": _string_list(row.get("included_columns")),
                "predicate": _clean_definition(row.get("predicate")),
                "expression": _clean_definition(row.get("expression")),
                "definition": _clean_definition(row.get("definition")),
                "comment": row.get("comment"),
            }
        )

    for row in rows["triggers"]:
        relation = relation_map.get(_relation_key(row))
        if relation is None:
            continue
        definition = _clean_definition(row.get("definition"))
        relation["triggers"].append(
            {
                "schema": relation["schema"],
                "relation": relation["name"],
                "name": str(row.get("trigger_name") or ""),
                "timing": row.get("timing"),
                "events": _string_list(row.get("events")),
                "level": row.get("level"),
                "enabled": row.get("enabled"),
                "function_schema": row.get("function_schema"),
                "function_signature": row.get("function_signature"),
                "definition": definition,
                "definition_hash": sha256_text(definition) if definition else None,
                "comment": row.get("comment"),
            }
        )

    for row in rows["policies"]:
        relation = relation_map.get(_relation_key(row))
        if relation is None:
            continue
        relation["policies"].append(
            {
                "schema": relation["schema"],
                "relation": relation["name"],
                "name": str(row.get("policy_name") or ""),
                "permissive": bool(row.get("permissive", False)),
                "command": row.get("command"),
                "target_roles": sorted(set(_string_list(row.get("target_roles")))),
                "using_expression": _clean_definition(row.get("using_expression")),
                "check_expression": _clean_definition(row.get("check_expression")),
            }
        )

    for relation in relation_map.values():
        relation["columns"].sort(
            key=lambda item: (item["ordinal_position"], item["name"])
        )
        relation["constraints"].sort(key=lambda item: (item["type"], item["name"]))
        relation["indexes"].sort(key=lambda item: item["name"])
        relation["triggers"].sort(key=lambda item: item["name"])
        relation["policies"].sort(key=lambda item: item["name"])
    return [relation_map[key] for key in sorted(relation_map)]


def _relation_key(row: Mapping[str, Any]) -> tuple[str, str]:
    return str(row.get("schema_name") or ""), str(row.get("relation_name") or "")


def _build_routines(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for row in rows:
        definition = _clean_definition(row.get("definition"))
        result.append(
            {
                "schema": str(row.get("schema_name") or ""),
                "name": str(row.get("routine_name") or ""),
                "signature": str(row.get("signature") or ""),
                "kind": str(row.get("routine_kind") or "function"),
                "result_type": row.get("result_type"),
                "language": row.get("language"),
                "volatility": row.get("volatility"),
                "parallel_safety": row.get("parallel_safety"),
                "security_definer": bool(row.get("security_definer", False)),
                "strict": bool(row.get("strict", False)),
                "owner": row.get("owner"),
                "acl": _acl_entries(row.get("acl")),
                "security_labels": _security_labels(row.get("security_labels")),
                "definition_hash": sha256_text(definition) if definition else None,
                "comment": row.get("comment"),
            }
        )
    return sorted(result, key=lambda item: (item["schema"], item["signature"]))


def _named_objects(
    rows: list[dict[str, Any]],
    source_name: str,
    target_name: str,
) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for row in rows:
        item = {
            key: value
            for key, value in row.items()
            if key not in {"schema_name", source_name}
        }
        item["schema"] = str(row.get("schema_name") or "")
        item[target_name] = str(row.get(source_name) or "")
        for key in ("default_expression", "definition"):
            if key in item:
                item[key] = _clean_definition(item[key])
        if "security_labels" in item:
            item["security_labels"] = _security_labels(item["security_labels"])
        result.append(item)
    return sorted(result, key=lambda item: (item["schema"], item[target_name]))


def _build_composite_types(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    grouped: dict[tuple[str, str], dict[str, Any]] = {}
    for row in rows:
        key = str(row.get("schema_name") or ""), str(row.get("type_name") or "")
        composite = grouped.setdefault(
            key,
            {
                "schema": key[0],
                "name": key[1],
                "owner": row.get("owner"),
                "security_labels": _security_labels(row.get("security_labels")),
                "comment": row.get("comment"),
                "attributes": [],
            },
        )
        composite["attributes"].append(
            {
                "name": str(row.get("attribute_name") or ""),
                "ordinal_position": int(row.get("ordinal_position") or 0),
                "data_type": row.get("data_type"),
                "nullable": bool(row.get("nullable", False)),
                "comment": row.get("attribute_comment"),
            }
        )
    for composite in grouped.values():
        composite["attributes"].sort(
            key=lambda item: (item["ordinal_position"], item["name"])
        )
    return [grouped[key] for key in sorted(grouped)]


def _build_partitions(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    result = []
    for row in rows:
        bound = _clean_definition(row.get("bound"))
        result.append(
            {
                "schema": str(row.get("schema_name") or ""),
                "name": str(row.get("partition_name") or ""),
                "parent_schema": row.get("parent_schema"),
                "parent_name": row.get("parent_name"),
                "strategy": row.get("strategy"),
                "key_definition": _clean_definition(row.get("key_definition")),
                "bound": bound,
                "default_partition": bound == "DEFAULT",
            }
        )
    return sorted(
        result,
        key=lambda item: (
            item["schema"],
            item["name"],
            str(item["parent_schema"]),
            str(item["parent_name"]),
        ),
    )


def _build_dependencies(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    unique: dict[str, dict[str, Any]] = {}
    for row in rows:
        source_schema = str(row.get("source_schema") or "")
        target_schema = str(row.get("target_schema") or "")
        source_identity = str(
            row.get("source_identity") or row.get("source_name") or ""
        )
        target_identity = str(
            row.get("target_identity") or row.get("target_name") or ""
        )
        source = _qualified_identity(source_schema, source_identity)
        target = _qualified_identity(target_schema, target_identity)
        item = {
            "source": source,
            "source_kind": row.get("source_kind"),
            "source_schema": source_schema,
            "source_name": row.get("source_name"),
            "target": target,
            "target_kind": row.get("target_kind"),
            "target_schema": target_schema,
            "target_name": row.get("target_name"),
            "kind": str(row.get("dependency_kind") or "normal"),
        }
        key = fingerprint(item)
        unique[key] = item
    return sorted(
        unique.values(),
        key=lambda item: (item["source"], item["target"], item["kind"]),
    )


def _qualified_identity(schema: str, identity: str) -> str:
    if not schema or identity.startswith(f"{schema}."):
        return identity
    return f"{schema}.{identity}"


def _build_rls_status(tables: list[dict[str, Any]]) -> list[dict[str, Any]]:
    return [
        {
            "schema": table["schema"],
            "relation": table["name"],
            "enabled": table["rls_enabled"],
            "forced": table["rls_forced"],
            "policies": [policy["name"] for policy in table["policies"]],
        }
        for table in tables
    ]


def _collect_comments(
    schema: str,
    schema_comment: Any,
    relations: list[dict[str, Any]],
    routines: list[dict[str, Any]],
    types: list[dict[str, Any]],
    extensions: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    comments: list[dict[str, Any]] = []

    def add(object_type: str, object_name: str, comment: Any) -> None:
        if isinstance(comment, str) and comment.strip():
            comments.append(
                {
                    "object_type": object_type,
                    "object": object_name,
                    "comment": normalize_text(comment),
                }
            )

    add("schema", schema, schema_comment)
    for relation in relations:
        relation_name = f"{schema}.{relation['name']}"
        add(relation["kind"], relation_name, relation.get("comment"))
        for column in relation["columns"]:
            add("column", f"{relation_name}.{column['name']}", column.get("comment"))
        for constraint in relation["constraints"]:
            add(
                "constraint",
                f"{relation_name}.{constraint['name']}",
                constraint.get("comment"),
            )
        for index in relation["indexes"]:
            add("index", f"{schema}.{index['name']}", index.get("comment"))
        for trigger in relation["triggers"]:
            add("trigger", f"{relation_name}.{trigger['name']}", trigger.get("comment"))
    for routine in routines:
        add(routine["kind"], f"{schema}.{routine['signature']}", routine.get("comment"))
    for data_type in types:
        add("type", f"{schema}.{data_type['name']}", data_type.get("comment"))
    for extension in extensions:
        add("extension", f"{schema}.{extension['name']}", extension.get("comment"))
    return sorted(comments, key=lambda item: (item["object_type"], item["object"]))


def _fingerprint_schema_objects(
    schema: dict[str, Any],
    object_fingerprints: dict[str, str],
) -> None:
    for relation in schema["relations"]:
        relation_identity = f"{relation['schema']}.{relation['name']}"
        for column in relation["columns"]:
            _attach_fingerprint(
                column,
                object_fingerprints,
                f"column:{relation_identity}.{column['name']}",
            )
        for constraint in relation["constraints"]:
            _attach_fingerprint(
                constraint,
                object_fingerprints,
                f"constraint:{relation_identity}.{constraint['name']}",
            )
        for index in relation["indexes"]:
            _attach_fingerprint(
                index,
                object_fingerprints,
                f"index:{relation['schema']}.{index['name']}",
            )
        for trigger in relation["triggers"]:
            _attach_fingerprint(
                trigger,
                object_fingerprints,
                f"trigger:{relation_identity}.{trigger['name']}",
            )
        for policy in relation["policies"]:
            _attach_fingerprint(
                policy,
                object_fingerprints,
                f"policy:{relation_identity}.{policy['name']}",
            )
        _attach_fingerprint(
            relation,
            object_fingerprints,
            f"{relation['kind']}:{relation_identity}",
        )

    for collection, kind in (
        ("functions", "function"),
        ("procedures", "procedure"),
        ("enums", "enum"),
        ("domains", "domain"),
        ("composite_types", "composite_type"),
        ("row_level_security", "rls"),
        ("partitions", "partition"),
        ("extensions", "extension"),
        ("comments", "comment"),
        ("dependencies", "dependency"),
    ):
        for item in schema[collection]:
            identity = _object_identity(kind, item)
            _attach_fingerprint(item, object_fingerprints, f"{kind}:{identity}")


def _object_identity(kind: str, item: Mapping[str, Any]) -> str:
    if kind in {"function", "procedure"}:
        return f"{item['schema']}.{item['signature']}"
    if kind in {"enum", "domain", "composite_type", "extension", "partition"}:
        return f"{item['schema']}.{item['name']}"
    if kind == "rls":
        return f"{item['schema']}.{item['relation']}"
    if kind == "comment":
        return f"{item['object_type']}:{item['object']}"
    if kind == "dependency":
        return f"{item['source']}->{item['target']}:{item['kind']}"
    return str(item.get("name") or fingerprint(item))


def _attach_fingerprint(
    item: dict[str, Any],
    object_fingerprints: dict[str, str],
    key: str,
) -> None:
    payload = {name: value for name, value in item.items() if name != "fingerprint"}
    item["fingerprint"] = fingerprint(payload)
    previous = object_fingerprints.get(key)
    if previous is not None and previous != item["fingerprint"]:
        raise ValueError(f"Catalog object fingerprint key collision: {key}")
    object_fingerprints[key] = item["fingerprint"]


def _acl_entries(value: Any) -> list[dict[str, Any]]:
    if value is None:
        return []
    if not isinstance(value, Sequence) or isinstance(value, (str, bytes, bytearray)):
        raise TypeError("Catalog ACL values must be arrays of mapping rows.")

    unique: dict[str, dict[str, Any]] = {}
    for raw_entry in value:
        if not isinstance(raw_entry, Mapping):
            raise TypeError("Catalog ACL entries must be mappings.")
        grantee = normalize_text(str(raw_entry.get("grantee") or ""))
        if grantee.casefold() == "public":
            grantee = "PUBLIC"
        entry = {
            "grantee": grantee,
            "grantor": normalize_text(str(raw_entry.get("grantor") or "")),
            "privilege": normalize_text(str(raw_entry.get("privilege") or "")).lower(),
            "grantable": bool(raw_entry.get("grantable", False)),
        }
        unique[fingerprint(entry)] = entry
    return sorted(
        unique.values(),
        key=lambda item: (
            item["grantee"],
            item["privilege"],
            item["grantor"],
            item["grantable"],
        ),
    )


def _security_labels(value: Any) -> list[dict[str, str]]:
    if value is None:
        return []
    if not isinstance(value, Sequence) or isinstance(value, (str, bytes, bytearray)):
        raise TypeError("Catalog security labels must be arrays of mapping rows.")

    unique: dict[str, dict[str, str]] = {}
    for raw_label in value:
        if not isinstance(raw_label, Mapping):
            raise TypeError("Catalog security-label entries must be mappings.")
        label = {
            "provider": normalize_text(str(raw_label.get("provider") or "")),
            "label": normalize_text(str(raw_label.get("label") or "")),
        }
        unique[fingerprint(label)] = label
    return sorted(
        unique.values(),
        key=lambda item: (item["provider"], item["label"]),
    )


def _string_list(value: Any) -> list[str]:
    if value is None:
        return []
    if isinstance(value, str):
        return [normalize_text(value)] if value else []
    if isinstance(value, Sequence) and not isinstance(value, (bytes, bytearray)):
        return [normalize_text(str(item)) for item in value if item is not None]
    return [normalize_text(str(value))]


def _clean_definition(value: Any) -> str | None:
    if value is None:
        return None
    text = normalize_text(str(value))
    lines = [line.rstrip() for line in text.split("\n")]
    cleaned = "\n".join(lines).strip()
    return cleaned or None
