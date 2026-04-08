# Changelog

> Auto-generated from git commit history using conventional commits.
<<<<<<< Updated upstream
> Generated: 2026-04-06 19:50:14 UTC
=======
> Generated: 2026-04-08 03:32:08 UTC
>>>>>>> Stashed changes

## Summary

| Category | Count |
|----------|-------|
<<<<<<< Updated upstream
| Bug Fixes | 1 |
| Documentation | 1 |
| **Total** | **2** |
=======
| Features | 2 |
| Bug Fixes | 3 |
| Performance | 4 |
| Documentation | 21 |
| Build & CI | 1 |
| Other | 19 |
| **Total** | **50** |

## Features

- **robinhood:** route option orders, fetch options positions/open orders, add DTOs and tests ([`fcb6b19`](https://github.com/rodoHasArrived/Meridian-main/commit/fcb6b19c1d12ec6d8ac5c64d089a2e267e43dc8a))
- options provider implementations, backtest context integration, and roadmap ([`b12c5a2`](https://github.com/rodoHasArrived/Meridian-main/commit/b12c5a2b8c1ab8ce055ce3593b8feb7f71f01626))

## Bug Fixes

- address code-review feedback - remove duplicate DTO class, fix blocking async in stubs, add log context ([`6465719`](https://github.com/rodoHasArrived/Meridian-main/commit/646571941b0f4a94d8644b68d90d991a1ce9e63e))
- resolve merge conflicts and narrow exception in score_eval.py ([`a066596`](https://github.com/rodoHasArrived/Meridian-main/commit/a06659682e98ac47af06231ebf3ca09c7d96e0f2))
- **ci:** remove PublishReadyToRun=true override causing NETSDK1096 on standalone publish ([`9c19795`](https://github.com/rodoHasArrived/Meridian-main/commit/9c19795d89b9400f674f66a7888dce38dc2bad57))

## Performance

- **robinhood:** concurrent HTTP fetches, const URLs, pre-sized collections, single-pass SortedSet ([`d78d814`](https://github.com/rodoHasArrived/Meridian-main/commit/d78d814b67981866123dac25908365ad17629e4e))
- add budget tests for combined-lock and ArrayPool fixes; update BOTTLENECK_REPORT ([`ff41164`](https://github.com/rodoHasArrived/Meridian-main/commit/ff41164c24c420b7cb5f05803ed956e47479fc01))
- address code review â€” guard TryFormat return, clarify payload.Length comment, document flush-task serialization ([`39c825e`](https://github.com/rodoHasArrived/Meridian-main/commit/39c825e881e93c199b424413edbcf2bcc54a5ca3))
- eliminate hot-path allocations across pipeline, WAL, and collector components ([`14b56ff`](https://github.com/rodoHasArrived/Meridian-main/commit/14b56ff35f23f1fedd708bc81d29a1d8799e9c6d))

## Documentation

- sync repository tree ([`155ba5b`](https://github.com/rodoHasArrived/Meridian-main/commit/155ba5ba86cb495adf7339ef12b88ab07c0ec120))
- consolidated documentation automation updates ([`7cf4bc6`](https://github.com/rodoHasArrived/Meridian-main/commit/7cf4bc6637d74535db0ccc5bf7240f498ad51c60))
- update TODO documentation [skip ci] ([`68207c8`](https://github.com/rodoHasArrived/Meridian-main/commit/68207c802d0de2e7b08127b34d1304351e68a4eb))
- sync repository tree ([`f74899e`](https://github.com/rodoHasArrived/Meridian-main/commit/f74899e4c8e21cd797540865a526dd92521020da))
- consolidated documentation automation updates ([`c5240ac`](https://github.com/rodoHasArrived/Meridian-main/commit/c5240ac070c935f6cb665abc5161e76b9a27d3c3))
- update TODO documentation [skip ci] ([`896d06f`](https://github.com/rodoHasArrived/Meridian-main/commit/896d06faad03857ad3b738488c1f4828e7f03ee5))
- sync repository tree ([`baee26d`](https://github.com/rodoHasArrived/Meridian-main/commit/baee26d2d343a6351bd05c99481e2f75c812b388))
- consolidated documentation automation updates ([`5dae060`](https://github.com/rodoHasArrived/Meridian-main/commit/5dae06078185989daabd4897d2bda4c4f410cb4d))
- update TODO documentation [skip ci] ([`0d96277`](https://github.com/rodoHasArrived/Meridian-main/commit/0d9627721776dbac6dfe54347e218768b80e9c26))
- consolidated documentation automation updates ([`a40a5a8`](https://github.com/rodoHasArrived/Meridian-main/commit/a40a5a8cc684e388150b1c0a832b3cb77e297796))
- update TODO documentation [skip ci] ([`a84dcae`](https://github.com/rodoHasArrived/Meridian-main/commit/a84dcae1c063a8167f3ad1dfb898b26445933974))
- consolidated documentation automation updates ([`f0b7eed`](https://github.com/rodoHasArrived/Meridian-main/commit/f0b7eed2c28bdd2e5138bf84d3fe4c4296d4f0b6))
- update TODO documentation [skip ci] ([`121c852`](https://github.com/rodoHasArrived/Meridian-main/commit/121c85225837eefd46c9984a8e1c9a409067cd62))
- sync repository tree ([`ff654aa`](https://github.com/rodoHasArrived/Meridian-main/commit/ff654aa6c36b68fa1474261ce3feb0b99393acd7))
- consolidated documentation automation updates ([`8d5158c`](https://github.com/rodoHasArrived/Meridian-main/commit/8d5158cfeaeb5b62140c831146eb5581417eae52))
- update TODO documentation [skip ci] ([`9a27071`](https://github.com/rodoHasArrived/Meridian-main/commit/9a27071d8fff1a81d5bf23d2581f5b7f16377525))
- sync repository tree ([`daacd06`](https://github.com/rodoHasArrived/Meridian-main/commit/daacd063a7fa60d483c7c1428046bdd857216c3a))
- consolidated documentation automation updates ([`7a358d6`](https://github.com/rodoHasArrived/Meridian-main/commit/7a358d64fb7c3df12f93f208c74579b5fffbc166))
- consolidated documentation automation updates ([`02a782e`](https://github.com/rodoHasArrived/Meridian-main/commit/02a782e3a1955e4f1db89130260bc7aa0fa57405))
- update TODO documentation [skip ci] ([`78f8723`](https://github.com/rodoHasArrived/Meridian-main/commit/78f8723f7b1e20a8818043f30bbc357bcd9be806))
- sync repository tree ([`8799953`](https://github.com/rodoHasArrived/Meridian-main/commit/879995348c3939dcb7f1293e860f32f4636725d9))

## Build & CI

- repair docfx build path and prompt templating ([`e5a36ee`](https://github.com/rodoHasArrived/Meridian-main/commit/e5a36ee694d963ee429d86da7fd321fe4c778d81))
>>>>>>> Stashed changes

## Bug Fixes

<<<<<<< Updated upstream
- resolve merge conflicts in score_eval.py ([`656c1f33`](https://github.com/rodoHasArrived/Meridian-main/commit/656c1f33c340918aad46a57cd62ac75d55c49dfc))

## Documentation

- regenerate all auto-generated docs and fix 24 broken links ([`b5e1da71`](https://github.com/rodoHasArrived/Meridian-main/commit/b5e1da718bc589193dfbbe3d2b83e5a22ae1fbc6))

---

*2 commits processed.*
=======
- Merge pull request #655 from rodoHasArrived/copilot/assess-options-functionality-roadmap [#655](https://github.com/rodoHasArrived/Meridian-main/issues/655) ([`2d889e0`](https://github.com/rodoHasArrived/Meridian-main/commit/2d889e0a9664d9618977040a088274c703e82996))
- Merge pull request #669 from rodoHasArrived/copilot/659-audit-codex-ci-repair-path [#669](https://github.com/rodoHasArrived/Meridian-main/issues/669) ([`bb5db8a`](https://github.com/rodoHasArrived/Meridian-main/commit/bb5db8aa1800a61b2f85bd9efbc2085f16bb3d40))
- Merge pull request #667 from rodoHasArrived/copilot/audit-robinhood-provider-functions [#667](https://github.com/rodoHasArrived/Meridian-main/issues/667) ([`64d7d8c`](https://github.com/rodoHasArrived/Meridian-main/commit/64d7d8cbf4b152ecdcc896fc7ad7aa5d69a17088))
- Merge branch 'main' of https://github.com/rodoHasArrived/Meridian-main ([`7d96dbe`](https://github.com/rodoHasArrived/Meridian-main/commit/7d96dbe3a4389e88185fbac13a97c6ac550e137c))
- Fix Robinhood provider: ProviderCredentialFields type and options asset class support ([`792beb8`](https://github.com/rodoHasArrived/Meridian-main/commit/792beb84ac44ebd5456df452932841dac0035e88))
- Initial plan ([`95555ed`](https://github.com/rodoHasArrived/Meridian-main/commit/95555edfb8afd9895d6c022de0c9c6988f076411))
- Merge pull request #664 from rodoHasArrived/copilot/evaluate-performance-increase [#664](https://github.com/rodoHasArrived/Meridian-main/issues/664) ([`b46adeb`](https://github.com/rodoHasArrived/Meridian-main/commit/b46adeb43544bef4ef6612926794aaa8c7c2a06e))
- Merge pull request #663 from rodoHasArrived/copilot/optimize-performance-constraints [#663](https://github.com/rodoHasArrived/Meridian-main/issues/663) ([`37f56f7`](https://github.com/rodoHasArrived/Meridian-main/commit/37f56f726a6df453338630bcf7bffd21b69d3678))
- Initial plan ([`f9ddede`](https://github.com/rodoHasArrived/Meridian-main/commit/f9ddede943b97370c2900803879199587c89e31a))
- Implement workstation shell UX and capture updated WPF screens ([`88704cc`](https://github.com/rodoHasArrived/Meridian-main/commit/88704ccc6a3b77ee914303f806c9fb61d5db52be))
- Initial plan ([`69826ba`](https://github.com/rodoHasArrived/Meridian-main/commit/69826bae62d8c9d79c400c0ca74922247d3fe29f))
- Complete Security Master productization across workstation surfaces ([`28cfb1e`](https://github.com/rodoHasArrived/Meridian-main/commit/28cfb1e53de80dce34b71688bfdd36430558867d))
- Merge branch 'main' of https://github.com/rodoHasArrived/Meridian-main ([`a627805`](https://github.com/rodoHasArrived/Meridian-main/commit/a627805926a72422bec44bf34d5a941c28b29504))
- Refresh roadmap docs for April 6 snapshot ([`91dcde1`](https://github.com/rodoHasArrived/Meridian-main/commit/91dcde15e4d88e3989673ed80803e47a606067ed))
- Merge pull request #658 from rodoHasArrived/codex/ci-repair-path-docfx [#658](https://github.com/rodoHasArrived/Meridian-main/issues/658) ([`f7f9e5d`](https://github.com/rodoHasArrived/Meridian-main/commit/f7f9e5d786a9f88e10ff19439c1c78eda1ec2dc5))
- Initial plan ([`1a4b972`](https://github.com/rodoHasArrived/Meridian-main/commit/1a4b972b6ea48f600b6fbf825c1313fc53c21c71))
- Merge pull request #653 from rodoHasArrived/copilot/fix-issue-with-workflow [#653](https://github.com/rodoHasArrived/Meridian-main/issues/653) ([`400c2ad`](https://github.com/rodoHasArrived/Meridian-main/commit/400c2addc85190b57680b9792e558a04ff2e3320))
- Initial plan ([`6ed1684`](https://github.com/rodoHasArrived/Meridian-main/commit/6ed16844d5b3ad62b24d985862200a224bbd1db2))
- Merge pull request #638 from rodoHasArrived/copilot/add-similar-functionality [#638](https://github.com/rodoHasArrived/Meridian-main/issues/638) ([`13bf807`](https://github.com/rodoHasArrived/Meridian-main/commit/13bf807aaed05266bd6480f74e502ba834f0c5cf))

---

*50 commits processed.*
>>>>>>> Stashed changes
