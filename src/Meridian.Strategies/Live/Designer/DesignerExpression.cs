using System.Globalization;
using Meridian.Core.Exceptions;

namespace Meridian.Strategies.Live.Designer;

/// <summary>The static result shape of a designer expression.</summary>
internal enum DesignerResultKind
{
    Number,
    Boolean
}

/// <summary>
/// Value produced by evaluating a <see cref="DesignerExpression"/>: either a number or a boolean.
/// </summary>
internal readonly struct DesignerValue
{
    private DesignerValue(decimal number, bool boolean, bool isBoolean)
    {
        Number = number;
        Boolean = boolean;
        IsBoolean = isBoolean;
    }

    public decimal Number { get; }

    public bool Boolean { get; }

    public bool IsBoolean { get; }

    public bool IsNumber => !IsBoolean;

    public static DesignerValue FromNumber(decimal value) => new(value, false, false);

    public static DesignerValue FromBoolean(bool value) => new(0m, value, true);
}

/// <summary>
/// Raised when designer source cannot be parsed into the closed expression grammar, or when a
/// bounded evaluation fault (a cold field, arithmetic overflow, division by zero) makes a value
/// unavailable. Carries the operator-facing message the live source turns into a fail-closed
/// deferral reason.
/// </summary>
internal sealed class DesignerExpressionException(string message) : MeridianException(message);

/// <summary>
/// A parsed, immutable expression over the Strategy Designer field catalog.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately <em>not</em> a script host. The grammar is closed — numeric literals,
/// catalog field identifiers, comparison, boolean, and arithmetic operators, and parentheses —
/// with no calls, no assignment, no member access, and no identifier that is not a catalog field.
/// That is what lets designer documents reach live execution without the isolation boundary
/// <c>PRD-012</c> requires for arbitrary C#: there is no code here to contain, because a document
/// cannot express anything outside this grammar.
/// </para>
/// <para>
/// Operand shapes are checked while parsing, so <see cref="ResultKind"/> is known before a run
/// activates. A filter that reads <c>PRICE</c> rather than a condition, or a rank that reads
/// <c>PRICE &gt; 20</c> rather than a score, is refused at compile time instead of activating and
/// then silently matching nothing on every event.
/// </para>
/// </remarks>
internal abstract class DesignerExpression
{
    /// <summary>Longest source accepted. Bounds parser work on operator-supplied text.</summary>
    private const int MaxSourceLength = 4096;

    /// <summary>
    /// Maximum bracket/unary nesting depth. Counted only where the parser actually recurses into
    /// a nested sub-expression, so the limit describes source nesting rather than the number of
    /// precedence levels the grammar happens to have.
    /// </summary>
    private const int MaxDepth = 32;

    /// <summary>The shape this expression evaluates to, known statically.</summary>
    public abstract DesignerResultKind ResultKind { get; }

    /// <summary>Evaluates against one symbol's resolved field values.</summary>
    public abstract DesignerValue Evaluate(IReadOnlyDictionary<string, decimal> fields);

    /// <summary>Catalog field ids this expression reads.</summary>
    public abstract IEnumerable<string> ReferencedFields();

    /// <summary>
    /// Evaluates as a gate. <see cref="ResultKind"/> is checked at compile time, so reaching this
    /// with a numeric expression is a programming error rather than a document error.
    /// </summary>
    public bool EvaluateCondition(IReadOnlyDictionary<string, decimal> fields)
    {
        var value = Evaluate(fields);
        return value.IsBoolean
            ? value.Boolean
            : throw new DesignerExpressionException(
                "Expression evaluated to a number where a true/false condition is required.");
    }

    /// <summary>
    /// Parses <paramref name="source"/> against <paramref name="knownFields"/>.
    /// </summary>
    /// <exception cref="DesignerExpressionException">
    /// The source is empty, too long, too deeply nested, references an identifier that is not a
    /// known catalog field, mixes operand shapes, or is not valid in the grammar.
    /// </exception>
    public static DesignerExpression Parse(string source, IReadOnlySet<string> knownFields)
    {
        ArgumentNullException.ThrowIfNull(knownFields);
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new DesignerExpressionException("Expression source is empty.");
        }

        if (source.Length > MaxSourceLength)
        {
            throw new DesignerExpressionException(
                $"Expression source is {source.Length} characters; the limit is {MaxSourceLength}.");
        }

        var parser = new Parser(Tokenizer.Tokenize(source), knownFields);
        var expression = parser.ParseExpression(depth: 0);
        parser.ExpectEnd();
        return expression;
    }

    /// <summary>
    /// Attempts a parse, additionally requiring <paramref name="requiredKind"/>, and returns the
    /// operator-facing failure message instead of throwing.
    /// </summary>
    public static bool TryParse(
        string source,
        IReadOnlySet<string> knownFields,
        DesignerResultKind requiredKind,
        out DesignerExpression? expression,
        out string? failureReason)
    {
        try
        {
            var parsed = Parse(source, knownFields);
            if (parsed.ResultKind != requiredKind)
            {
                expression = null;
                failureReason = requiredKind == DesignerResultKind.Boolean
                    ? "Expected a true/false condition (for example 'PRICE > 20'), but the source is a numeric value."
                    : "Expected a numeric score (for example 'MOMENTUM_63D - VOLATILITY_20D'), but the source is a true/false condition.";
                return false;
            }

            expression = parsed;
            failureReason = null;
            return true;
        }
        catch (DesignerExpressionException ex)
        {
            expression = null;
            failureReason = ex.Message;
            return false;
        }
    }

    private sealed class Literal(decimal value) : DesignerExpression
    {
        public override DesignerResultKind ResultKind => DesignerResultKind.Number;

        public override DesignerValue Evaluate(IReadOnlyDictionary<string, decimal> fields) =>
            DesignerValue.FromNumber(value);

        public override IEnumerable<string> ReferencedFields() => Array.Empty<string>();
    }

    private sealed class Field(string fieldId) : DesignerExpression
    {
        public override DesignerResultKind ResultKind => DesignerResultKind.Number;

        public override DesignerValue Evaluate(IReadOnlyDictionary<string, decimal> fields) =>
            fields.TryGetValue(fieldId, out var value)
                ? DesignerValue.FromNumber(value)
                // Reached only when the evaluator has no observation yet for this symbol. Failing
                // rather than defaulting keeps a cold field from reading as 0 and tripping a
                // "< threshold" filter into admitting a symbol it should have excluded.
                : throw new DesignerExpressionException($"Field '{fieldId}' has no value for this symbol yet.");

        public override IEnumerable<string> ReferencedFields() => new[] { fieldId };
    }

    private sealed class Not(DesignerExpression inner) : DesignerExpression
    {
        public override DesignerResultKind ResultKind => DesignerResultKind.Boolean;

        public override DesignerValue Evaluate(IReadOnlyDictionary<string, decimal> fields) =>
            DesignerValue.FromBoolean(!inner.Evaluate(fields).Boolean);

        public override IEnumerable<string> ReferencedFields() => inner.ReferencedFields();
    }

    private sealed class Negate(DesignerExpression inner) : DesignerExpression
    {
        public override DesignerResultKind ResultKind => DesignerResultKind.Number;

        public override DesignerValue Evaluate(IReadOnlyDictionary<string, decimal> fields) =>
            DesignerValue.FromNumber(Checked(() => -inner.Evaluate(fields).Number));

        public override IEnumerable<string> ReferencedFields() => inner.ReferencedFields();
    }

    private sealed class Binary(string op, DesignerExpression left, DesignerExpression right) : DesignerExpression
    {
        public override DesignerResultKind ResultKind => op is "+" or "-" or "*" or "/"
            ? DesignerResultKind.Number
            : DesignerResultKind.Boolean;

        public override DesignerValue Evaluate(IReadOnlyDictionary<string, decimal> fields)
        {
            // Short-circuit before evaluating the right operand so a guard like
            // "PORTFOLIO_WEIGHT == 0 || MOMENTUM_63D > 0" still answers when the second field is
            // not yet warm.
            if (op is "&&" or "||")
            {
                var leftGate = left.Evaluate(fields).Boolean;
                if (op == "&&" && !leftGate)
                {
                    return DesignerValue.FromBoolean(false);
                }

                if (op == "||" && leftGate)
                {
                    return DesignerValue.FromBoolean(true);
                }

                return DesignerValue.FromBoolean(right.Evaluate(fields).Boolean);
            }

            var leftValue = left.Evaluate(fields);
            var rightValue = right.Evaluate(fields);

            if (op is "==" or "!=")
            {
                var equal = leftValue.IsBoolean
                    ? leftValue.Boolean == rightValue.Boolean
                    : leftValue.Number == rightValue.Number;
                return DesignerValue.FromBoolean(op == "==" ? equal : !equal);
            }

            var a = leftValue.Number;
            var b = rightValue.Number;
            return op switch
            {
                ">" => DesignerValue.FromBoolean(a > b),
                ">=" => DesignerValue.FromBoolean(a >= b),
                "<" => DesignerValue.FromBoolean(a < b),
                "<=" => DesignerValue.FromBoolean(a <= b),
                "+" => DesignerValue.FromNumber(Checked(() => a + b)),
                "-" => DesignerValue.FromNumber(Checked(() => a - b)),
                "*" => DesignerValue.FromNumber(Checked(() => a * b)),
                "/" => b == 0m
                    ? throw new DesignerExpressionException("Division by zero.")
                    : DesignerValue.FromNumber(Checked(() => a / b)),
                _ => throw new DesignerExpressionException($"Unsupported operator '{op}'.")
            };
        }

        public override IEnumerable<string> ReferencedFields() =>
            left.ReferencedFields().Concat(right.ReferencedFields());
    }

    /// <summary>
    /// Runs a decimal operation, converting an overflow into the bounded evaluation failure the
    /// strategy already handles. Letting <see cref="OverflowException"/> escape would take down a
    /// market-event callback rather than skipping the symbol.
    /// </summary>
    private static decimal Checked(Func<decimal> operation)
    {
        try
        {
            return operation();
        }
        catch (OverflowException)
        {
            throw new DesignerExpressionException("Arithmetic overflow while evaluating the expression.");
        }
    }

    private enum TokenKind
    {
        Number,
        Identifier,
        Operator,
        OpenParen,
        CloseParen,
        End
    }

    private readonly record struct Token(TokenKind Kind, string Text, decimal Number);

    private static class Tokenizer
    {
        private static readonly string[] TwoCharOperators = [">=", "<=", "==", "!=", "&&", "||"];

        public static IReadOnlyList<Token> Tokenize(string source)
        {
            var tokens = new List<Token>();
            var index = 0;
            while (index < source.Length)
            {
                var current = source[index];
                if (char.IsWhiteSpace(current))
                {
                    index++;
                    continue;
                }

                if (current == '(')
                {
                    tokens.Add(new Token(TokenKind.OpenParen, "(", 0m));
                    index++;
                    continue;
                }

                if (current == ')')
                {
                    tokens.Add(new Token(TokenKind.CloseParen, ")", 0m));
                    index++;
                    continue;
                }

                if (char.IsAsciiDigit(current) || (current == '.' && index + 1 < source.Length && char.IsAsciiDigit(source[index + 1])))
                {
                    index = ReadNumber(source, index, tokens);
                    continue;
                }

                if (char.IsAsciiLetter(current) || current == '_')
                {
                    index = ReadIdentifier(source, index, tokens);
                    continue;
                }

                if (index + 1 < source.Length)
                {
                    var pair = source.Substring(index, 2);
                    if (Array.IndexOf(TwoCharOperators, pair) >= 0)
                    {
                        tokens.Add(new Token(TokenKind.Operator, pair, 0m));
                        index += 2;
                        continue;
                    }
                }

                if (current is '>' or '<' or '+' or '-' or '*' or '/' or '!')
                {
                    tokens.Add(new Token(TokenKind.Operator, current.ToString(), 0m));
                    index++;
                    continue;
                }

                throw new DesignerExpressionException(
                    $"Unexpected character '{current}' at position {index}.");
            }

            tokens.Add(new Token(TokenKind.End, string.Empty, 0m));
            return tokens;
        }

        private static int ReadNumber(string source, int index, List<Token> tokens)
        {
            var start = index;
            var seenDot = false;
            while (index < source.Length && (char.IsAsciiDigit(source[index]) || (source[index] == '.' && !seenDot)))
            {
                seenDot |= source[index] == '.';
                index++;
            }

            var text = source[start..index];

            // A trailing letter marks a suffixed literal the grammar does not define -- "45d" in a
            // prototype expiry rule, say. Rejecting it keeps "45d" from silently parsing as 45.
            if (index < source.Length && (char.IsAsciiLetter(source[index]) || source[index] == '_'))
            {
                var suffixStart = index;
                while (index < source.Length && (char.IsAsciiLetterOrDigit(source[index]) || source[index] == '_'))
                {
                    index++;
                }

                throw new DesignerExpressionException(
                    $"Numeric literal '{text}{source[suffixStart..index]}' has an unsupported suffix; " +
                    "designer expressions accept plain numbers only.");
            }

            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                throw new DesignerExpressionException($"'{text}' is not a valid number.");
            }

            tokens.Add(new Token(TokenKind.Number, text, value));
            return index;
        }

        private static int ReadIdentifier(string source, int index, List<Token> tokens)
        {
            var start = index;
            while (index < source.Length && (char.IsAsciiLetterOrDigit(source[index]) || source[index] == '_'))
            {
                index++;
            }

            tokens.Add(new Token(TokenKind.Identifier, source[start..index], 0m));
            return index;
        }
    }

    private sealed class Parser(IReadOnlyList<Token> tokens, IReadOnlySet<string> knownFields)
    {
        private int _position;

        private Token Current => tokens[_position];

        public void ExpectEnd()
        {
            if (Current.Kind != TokenKind.End)
            {
                throw new DesignerExpressionException(
                    $"Unexpected '{Current.Text}' after a complete expression.");
            }
        }

        public DesignerExpression ParseExpression(int depth) => ParseOr(depth);

        private DesignerExpression ParseOr(int depth)
        {
            var left = ParseAnd(depth);
            while (Current is { Kind: TokenKind.Operator, Text: "||" })
            {
                _position++;
                left = Combine("||", left, ParseAnd(depth));
            }

            return left;
        }

        private DesignerExpression ParseAnd(int depth)
        {
            var left = ParseComparison(depth);
            while (Current is { Kind: TokenKind.Operator, Text: "&&" })
            {
                _position++;
                left = Combine("&&", left, ParseComparison(depth));
            }

            return left;
        }

        private DesignerExpression ParseComparison(int depth)
        {
            var left = ParseAdditive(depth);
            if (Current.Kind == TokenKind.Operator
                && Current.Text is ">" or ">=" or "<" or "<=" or "==" or "!=")
            {
                var op = Current.Text;
                _position++;
                // Not a loop: "a < b < c" is a modelling mistake, not a chained comparison, and
                // ExpectEnd surfaces it rather than quietly comparing a boolean against a number.
                return Combine(op, left, ParseAdditive(depth));
            }

            return left;
        }

        private DesignerExpression ParseAdditive(int depth)
        {
            var left = ParseMultiplicative(depth);
            while (Current.Kind == TokenKind.Operator && Current.Text is "+" or "-")
            {
                var op = Current.Text;
                _position++;
                left = Combine(op, left, ParseMultiplicative(depth));
            }

            return left;
        }

        private DesignerExpression ParseMultiplicative(int depth)
        {
            var left = ParseUnary(depth);
            while (Current.Kind == TokenKind.Operator && Current.Text is "*" or "/")
            {
                var op = Current.Text;
                _position++;
                left = Combine(op, left, ParseUnary(depth));
            }

            return left;
        }

        private DesignerExpression ParseUnary(int depth)
        {
            if (Current is { Kind: TokenKind.Operator, Text: "!" })
            {
                _position++;
                var operand = ParseUnary(Deepen(depth));
                return operand.ResultKind == DesignerResultKind.Boolean
                    ? new Not(operand)
                    : throw new DesignerExpressionException("'!' requires a true/false operand.");
            }

            if (Current is { Kind: TokenKind.Operator, Text: "-" })
            {
                _position++;
                var operand = ParseUnary(Deepen(depth));
                return operand.ResultKind == DesignerResultKind.Number
                    ? new Negate(operand)
                    : throw new DesignerExpressionException("Unary '-' requires a numeric operand.");
            }

            return ParsePrimary(depth);
        }

        private DesignerExpression ParsePrimary(int depth)
        {
            if (Current.Kind == TokenKind.Number)
            {
                var value = Current.Number;
                _position++;
                return new Literal(value);
            }

            if (Current.Kind == TokenKind.Identifier)
            {
                var name = Current.Text;
                _position++;

                // The closed identifier set is the containment property: anything that is not a
                // catalog field -- a method name, a keyword, a smuggled symbol -- stops here.
                if (!knownFields.Contains(name))
                {
                    throw new DesignerExpressionException(
                        $"'{name}' is not a Strategy Designer catalog field.");
                }

                return new Field(name);
            }

            if (Current.Kind == TokenKind.OpenParen)
            {
                _position++;
                var inner = ParseExpression(Deepen(depth));
                if (Current.Kind != TokenKind.CloseParen)
                {
                    throw new DesignerExpressionException("Missing ')'.");
                }

                _position++;
                return inner;
            }

            throw new DesignerExpressionException(
                Current.Kind == TokenKind.End
                    ? "Expression ended unexpectedly."
                    : $"Unexpected '{Current.Text}'.");
        }

        /// <summary>
        /// Builds a binary node after checking both operand shapes, so a mismatch is a parse
        /// failure the operator sees rather than a run that activates and never matches.
        /// </summary>
        private static DesignerExpression Combine(string op, DesignerExpression left, DesignerExpression right)
        {
            var required = op switch
            {
                "&&" or "||" => DesignerResultKind.Boolean,
                "==" or "!=" => left.ResultKind,
                _ => DesignerResultKind.Number
            };

            if (left.ResultKind != required || right.ResultKind != required)
            {
                throw new DesignerExpressionException(
                    required == DesignerResultKind.Boolean
                        ? $"'{op}' requires true/false operands."
                        : $"'{op}' requires numeric operands.");
            }

            return new Binary(op, left, right);
        }

        private static int Deepen(int depth) => depth < MaxDepth
            ? depth + 1
            : throw new DesignerExpressionException(
                $"Expression nests deeper than the supported limit of {MaxDepth}.");
    }
}
