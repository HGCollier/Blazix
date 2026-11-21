using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Blazix.Docs.Components.Docs;

/// <summary>
/// Syntax highlighting for the code the docs show (Razor, C#, JavaScript, CSS, bash, JSON, diff), as VS Code shows it in its
/// Light Modern and Dark Modern themes with the C# extension: the scanners approximate the TextMate grammars, and for Razor
/// and C# what the extension's semantic tokens change (component tags and parameters, directive attributes, C# values in
/// attributes, types, enum members, constants and methods by reflection over the loaded assemblies). Each token kind maps to
/// the colour its scope gets; each token carries a light and a dark colour as <c>--shiki-light</c>/<c>--shiki-dark</c>, and
/// <see cref="CodeView"/> picks one.
/// </summary>
internal static partial class Highlighter
{
    private enum Kind
    {
        /// <summary>The default foreground: punctuation, operators and plain text.</summary>
        Text,

        /// <summary>Variables, properties, fields and parameters.</summary>
        Variable,

        /// <summary>Storage, modifier and other keywords (<c>var</c>, <c>public</c>, <c>class</c>, <c>new</c>).</summary>
        Keyword,

        /// <summary>A C# operator, including member access's <c>.</c> (Roslyn's operator classification).</summary>
        Operator,

        /// <summary>Control-flow keywords (<c>if</c>, <c>return</c>, <c>await</c>, <c>import</c>) and CSS at-rules.</summary>
        Control,

        /// <summary>Language constants (<c>true</c>, <c>null</c>, <c>this</c>) and character entities.</summary>
        Constant,

        Number,
        String,

        /// <summary>A markup attribute's quoted value.</summary>
        AttributeValue,

        Escape,
        Regex,
        Function,

        /// <summary>A type or namespace, and a Razor component's tag and parameter names.</summary>
        Type,

        Tag,

        /// <summary>A tag's <c>&lt;</c>, <c>&gt;</c>, <c>&lt;/</c> and <c>/&gt;</c>.</summary>
        TagPunctuation,

        /// <summary>A markup attribute's name, and a CSS property's.</summary>
        Attribute,

        Comment,

        /// <summary>
        /// A JavaScript constant (<c>UPPER_CASE</c> names and <c>const</c> declarations), and a C# constant or enum member.
        /// </summary>
        ConstantVariable,

        /// <summary>A CSS selector's type, class, id or pseudo-class.</summary>
        CssSelector,

        /// <summary>A CSS value keyword (<c>auto</c>, <c>none</c>) or hex colour.</summary>
        CssValue,

        JsonKey,
        Inserted,
        Deleted,
    }

    // Each kind's colour in Light Modern and Dark Modern: its TextMate scope's, or for Razor and C# the scope VS Code's C#
    // extension maps its semantic token to (razorComponentElement is entity.name.class, enumMember variable.other.enummember...).
    // A few are a shade darker (light) or lighter (dark) than VS Code's, so every colour has at least 4.5:1 contrast with
    // the code background and the highlighted line's (WCAG AA): Number, Escape, Type, Attribute and Inserted in light,
    // Regex and TagPunctuation in dark.
    private static readonly Dictionary<Kind, (string Light, string Dark)> Colors = new()
    {
        [Kind.Text] = ("#3B3B3B", "#CCCCCC"),
        [Kind.Variable] = ("#001080", "#9CDCFE"),
        [Kind.Keyword] = ("#0000FF", "#569CD6"),
        [Kind.Operator] = ("#000000", "#D4D4D4"),
        [Kind.Control] = ("#AF00DB", "#C586C0"),
        [Kind.Constant] = ("#0000FF", "#569CD6"),
        [Kind.Number] = ("#087C52", "#B5CEA8"),
        [Kind.String] = ("#A31515", "#CE9178"),
        [Kind.AttributeValue] = ("#0000FF", "#CE9178"),
        [Kind.Escape] = ("#D90000", "#D7BA7D"),
        [Kind.Regex] = ("#811F3F", "#D47272"),
        [Kind.Function] = ("#795E26", "#DCDCAA"),
        [Kind.Type] = ("#23768E", "#4EC9B0"),
        [Kind.Tag] = ("#800000", "#569CD6"),
        [Kind.TagPunctuation] = ("#800000", "#8E8E8E"),
        [Kind.Attribute] = ("#DB0000", "#9CDCFE"),
        [Kind.Comment] = ("#008000", "#6A9955"),
        [Kind.ConstantVariable] = ("#0070C1", "#4FC1FF"),
        [Kind.CssSelector] = ("#800000", "#D7BA7D"),
        [Kind.CssValue] = ("#0451A5", "#CE9178"),
        [Kind.JsonKey] = ("#0451A5", "#9CDCFE"),
        [Kind.Inserted] = ("#087C52", "#B5CEA8"),
        [Kind.Deleted] = ("#A31515", "#CE9178"),
    };

    private static readonly ConcurrentDictionary<(string Language, string Code), IReadOnlyList<string>> Cache = new();

    /// <summary>The code as one HTML string per line.</summary>
    public static IReadOnlyList<string> Lines(string code, string language) =>
        Cache.GetOrAdd((language, code), static key => ToLines(Tokenize(key.Code.Replace("\r\n", "\n"), key.Language)));

    private static List<(Kind Kind, string Text)> Tokenize(string code, string language)
    {
        var tokens = new List<(Kind Kind, string Text)>();
        switch (language)
        {
            case "razor" or "cshtml" or "html":
                new RazorScanner(code, tokens, new SnippetInfo(code)).Scan();
                break;
            case "csharp" or "cs" or "c#":
                new CSharpScanner(code, tokens, new SnippetInfo(code)).Scan(0, code.Length);
                break;
            case "js" or "javascript" or "mjs" or "ts" or "typescript":
                new JsScanner(code, tokens).Scan(0, code.Length);
                break;
            case "css":
                new CssScanner(code, tokens).Scan(0, code.Length);
                break;
            case "bash" or "sh" or "shell":
                Bash(code, tokens);
                break;
            case "json":
                Json(code, tokens);
                break;
            case "diff":
                Diff(code, tokens);
                break;
            default:
                tokens.Add((Kind.Text, code));
                break;
        }
        return tokens;
    }

    /// <summary>One HTML string per line: a span per run of one colour (whitespace joins the run before it).</summary>
    private static List<string> ToLines(List<(Kind Kind, string Text)> tokens)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        var run = new StringBuilder();
        (string Light, string Dark)? runColors = null;

        void EndRun()
        {
            if (run.Length > 0 && runColors is { } colors)
            {
                line.Append("<span style=\"--shiki-light:").Append(colors.Light).Append(";--shiki-dark:").Append(colors.Dark)
                    .Append("\">").Append(WebUtility.HtmlEncode(run.ToString())).Append("</span>");
            }
            run.Clear();
        }

        foreach (var (kind, text) in tokens)
        {
            var colors = Colors[kind];
            var parts = text.Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    EndRun();
                    runColors = null;
                    lines.Add(line.ToString());
                    line.Clear();
                }
                var part = parts[i];
                if (part.Length == 0)
                {
                    continue;
                }
                if (runColors is not null && (string.IsNullOrWhiteSpace(part) || runColors == colors))
                {
                    run.Append(part);
                    continue;
                }
                EndRun();
                runColors = colors;
                run.Append(part);
            }
        }
        EndRun();
        lines.Add(line.ToString());
        return lines;
    }

    // ---- Razor ----------------------------------------------------------------------------------

    /// <summary>
    /// Razor markup, with the C# in it: directives, @code, @(...), implicit expressions and control flow. A control-flow
    /// block's body is C#, until a tag switches back to markup for that element (Razor's code/markup transitions).
    /// </summary>
    private sealed class RazorScanner(string s, List<(Kind Kind, string Text)> tokens, SnippetInfo info)
    {
        private static readonly HashSet<string> VoidElements =
            ["area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"];

        private static readonly HashSet<string> Statements =
            ["if", "for", "foreach", "while", "switch", "lock", "using", "do", "try", "else", "catch", "finally"];

        private static readonly HashSet<string> TypeDirectives = ["implements", "inherits", "layout", "typeparam", "namespace"];

        private static readonly HashSet<string> Directives = ["page", "inject", "attribute", "rendermode", "preservewhitespace"];

        /// <summary>The open code blocks and elements: the innermost says whether text is C# or markup.</summary>
        private readonly List<Frame> _frames = [];

        private sealed record Frame(bool Code, string? Element, bool KeywordBraces);

        /// <summary>The component whose tag is being scanned, for its parameters; null in an HTML element's tag.</summary>
        private string? _component;

        /// <summary>How the value of the attribute just named is highlighted.</summary>
        private ValueKind _valueKind;

        private bool _afterEquals;

        private bool InCode => _frames.Count > 0 && _frames[^1].Code;

        public void Scan()
        {
            var i = 0;
            while (i < s.Length)
            {
                i = InCode ? CodeStep(i) : MarkupStep(i);
            }
        }

        private void Add(Kind kind, string text)
        {
            if (text.Length > 0)
            {
                tokens.Add((kind, text));
            }
        }

        private int MarkupStep(int i)
        {
            var c = s[i];
            if (Starts(s, i, "@*"))
            {
                return RazorComment(i);
            }
            if (Starts(s, i, "<!--"))
            {
                var end = s.IndexOf("-->", i + 4, StringComparison.Ordinal);
                end = end < 0 ? s.Length : end + 3;
                Add(Kind.Comment, s[i..end]);
                return end;
            }
            if (Starts(s, i, "@@"))
            {
                Add(Kind.Text, "@@");
                return i + 2;
            }
            if (c == '@' && (i == 0 || !char.IsLetterOrDigit(s[i - 1])))
            {
                return Transition(i);
            }
            if (IsTagStart(i))
            {
                return Tag(i);
            }
            if (c == '&' && EntityEnd(s, i, s.Length) is var entity && entity > i)
            {
                Add(Kind.Constant, s[i..entity]);
                return entity;
            }
            if (c == '}' && LineStartsAt(i) && _frames.Exists(frame => frame.Code))
            {
                // An element left open in a code block (a snippet that elides its end): the block's brace still closes it.
                while (!_frames[^1].Code)
                {
                    _frames.RemoveAt(_frames.Count - 1);
                }
                return CloseBlock(i);
            }
            var k = i + 1;
            while (k < s.Length && s[k] is not ('@' or '<' or '&' or '}'))
            {
                k++;
            }
            Add(Kind.Text, s[i..k]);
            return k;
        }

        private int CodeStep(int i)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
            {
                var k = i;
                while (k < s.Length && char.IsWhiteSpace(s[k]))
                {
                    k++;
                }
                Add(Kind.Text, s[i..k]);
                return k;
            }
            if (c == '}')
            {
                return CloseBlock(i);
            }
            if (c == '{')
            {
                Add(Kind.Text, "{");
                _frames.Add(new Frame(Code: true, Element: null, KeywordBraces: false));
                return i + 1;
            }
            if (IsTagStart(i))
            {
                return Tag(i);
            }
            if (Starts(s, i, "@*"))
            {
                return RazorComment(i);
            }
            if (Starts(s, i, "@:"))
            {
                var end = LineEnd(s, i, s.Length);
                Add(Kind.Control, "@:");
                Add(Kind.Text, s[(i + 2)..end]);
                return end;
            }
            if (c == '@')
            {
                return Transition(i);
            }
            var word = WordAt(i);
            if (Statements.Contains(word))
            {
                return Statement(i, word, razor: false);
            }
            var statementEnd = Math.Max(i + 1, StatementEnd(i));
            new CSharpScanner(s, tokens, info).Scan(i, statementEnd);
            return statementEnd;
        }

        /// <summary>The <c>}</c> that ends the innermost code block, and an <c>else</c>, <c>catch</c> or <c>finally</c> that follows it.</summary>
        private int CloseBlock(int i)
        {
            var frame = _frames[^1];
            _frames.RemoveAt(_frames.Count - 1);
            Add(frame.KeywordBraces ? Kind.Control : Kind.Text, "}");
            var next = SkipWhitespace(i + 1);
            var word = WordAt(next);
            if (word is "else" or "catch" or "finally")
            {
                Add(Kind.Text, s[(i + 1)..next]);
                return Statement(next, word, razor: false);
            }
            return i + 1;
        }

        /// <summary>A control-flow statement: its keyword, its condition in C#, then the block it opens.</summary>
        private int Statement(int i, string word, bool razor)
        {
            var k = i + (razor ? 1 : 0) + word.Length;
            Add(Kind.Control, s[i..k]);
            if (word == "else")
            {
                var w = SkipWhitespace(k);
                if (WordAt(w) == "if")
                {
                    Add(Kind.Text, s[k..w]);
                    Add(Kind.Control, "if");
                    k = w + 2;
                }
            }
            var paren = SkipWhitespace(k);
            if (paren < s.Length && s[paren] == '(')
            {
                Add(Kind.Text, s[k..paren]);
                var close = Matching(s, paren, '(', ')');
                new CSharpScanner(s, tokens, info).Scan(paren, close);
                k = close;
            }
            var brace = SkipWhitespace(k);
            if (brace < s.Length && s[brace] == '{')
            {
                Add(Kind.Text, s[k..(brace + 1)]);
                _frames.Add(new Frame(Code: true, Element: null, KeywordBraces: false));
                return brace + 1;
            }
            return k;
        }

        /// <summary>An @ transition: a directive, @code, @(...), @{...}, control flow or an implicit expression.</summary>
        private int Transition(int i)
        {
            var start = i + 1;
            if (start >= s.Length)
            {
                Add(Kind.Text, "@");
                return start;
            }
            if (s[start] == '(')
            {
                return ExplicitExpression(i);
            }
            if (s[start] == '{')
            {
                Add(Kind.Control, "@{");
                _frames.Add(new Frame(Code: true, Element: null, KeywordBraces: true));
                return start + 1;
            }
            var word = WordAt(start);
            var j = start + word.Length;
            if (word.Length == 0)
            {
                Add(Kind.Text, "@");
                return start;
            }
            if (word is "code" or "functions")
            {
                return CodeDirective(i, j);
            }
            var afterWord = SkipWhitespace(j);
            if (word == "using" && (afterWord >= s.Length || s[afterWord] != '('))
            {
                return UsingDirective(j);
            }
            if (Directives.Contains(word) || TypeDirectives.Contains(word))
            {
                var end = LineEnd(s, j, s.Length);
                Add(Kind.Control, "@" + word);
                if (TypeDirectives.Contains(word))
                {
                    TypeNames(j, end);
                }
                else
                {
                    new CSharpScanner(s, tokens, info).Scan(j, end);
                }
                return end;
            }
            if (Statements.Contains(word))
            {
                return Statement(i, word, razor: true);
            }
            Add(Kind.Control, "@");
            var expressionEnd = ImplicitEnd(j, s.Length);
            new CSharpScanner(s, tokens, info).Scan(start, expressionEnd);
            return expressionEnd;
        }

        private int ExplicitExpression(int i, bool inAttribute = false)
        {
            Add(Kind.Control, "@(");
            var close = Matching(s, i + 1, '(', ')');
            var closed = close > i + 2 && s[close - 1] == ')';
            new CSharpScanner(s, tokens, info) { InAttribute = inAttribute }.Scan(i + 2, closed ? close - 1 : close);
            if (closed)
            {
                Add(Kind.Control, ")");
            }
            return close;
        }

        private int CodeDirective(int i, int j)
        {
            Add(Kind.Control, s[i..j]);
            var brace = s.IndexOf('{', j);
            if (brace < 0)
            {
                return j;
            }
            Add(Kind.Text, s[j..brace]);
            Add(Kind.Control, "{");
            var close = Matching(s, brace, '{', '}');
            var closed = close > brace + 1 && s[close - 1] == '}';
            new CSharpScanner(s, tokens, info).Scan(brace + 1, closed ? close - 1 : close);
            if (closed)
            {
                Add(Kind.Control, "}");
            }
            return close;
        }

        private int UsingDirective(int j)
        {
            Add(Kind.Control, "@using");
            var end = LineEnd(s, j, s.Length);
            TypeNames(j, end);
            return end;
        }

        /// <summary>A directive's namespace or type names, e.g. <c>@using Blazix.Primitives</c> or <c>@implements IDisposable</c>.</summary>
        private void TypeNames(int start, int end)
        {
            var k = start;
            while (k < end)
            {
                var word = WordAt(k);
                if (word.Length > 0)
                {
                    Add(word is "static" or "global" || CSharpKeywords.Contains(word) ? Kind.Keyword : Kind.Type, word);
                    k += word.Length;
                    continue;
                }
                Add(s[k] is '=' or '.' ? Kind.Operator : Kind.Text, s[k].ToString());
                k++;
            }
        }

        private int RazorComment(int i)
        {
            var end = s.IndexOf("*@", i + 2, StringComparison.Ordinal);
            Add(Kind.Control, "@*");
            if (end < 0)
            {
                Add(Kind.Comment, s[(i + 2)..]);
                return s.Length;
            }
            Add(Kind.Comment, s[(i + 2)..end]);
            Add(Kind.Control, "*@");
            return end + 2;
        }

        private bool IsTagStart(int i) =>
            s[i] == '<' && i + 1 < s.Length && (char.IsLetter(s[i + 1]) || (s[i + 1] == '/' && i + 2 < s.Length && char.IsLetter(s[i + 2])));

        private int Tag(int i)
        {
            var j = i + 1;
            var closing = s[j] == '/';
            if (closing)
            {
                j++;
            }
            var nameStart = j;
            while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] is '-' or '.' or ':' or '_'))
            {
                j++;
            }
            var name = s[nameStart..j];
            var component = IsComponentTag(name);
            Add(Kind.TagPunctuation, closing ? "</" : "<");
            Add(component ? Kind.Type : Kind.Tag, name);
            _component = component ? name : null;
            _afterEquals = false;

            var selfClosing = false;
            while (j < s.Length)
            {
                var c = s[j];
                if (c == '>')
                {
                    Add(Kind.TagPunctuation, ">");
                    j++;
                    break;
                }
                if (c == '/' && j + 1 < s.Length && s[j + 1] == '>')
                {
                    Add(Kind.TagPunctuation, "/>");
                    j += 2;
                    selfClosing = true;
                    break;
                }
                j = TagPart(j);
            }
            _component = null;

            if (closing)
            {
                PopElement(name);
            }
            else if (!selfClosing && !VoidElements.Contains(name.ToLowerInvariant()))
            {
                if (name.Equals("script", StringComparison.OrdinalIgnoreCase) || name.Equals("style", StringComparison.OrdinalIgnoreCase))
                {
                    return EmbeddedCode(j, name.ToLowerInvariant());
                }
                _frames.Add(new Frame(Code: false, Element: name, KeywordBraces: false));
            }
            return j;
        }

        /// <summary>A component's tag (Razor's rule: a name, or a dotted name's last part, that starts with a capital).</summary>
        private static bool IsComponentTag(string name)
        {
            var last = name[(name.LastIndexOf('.') + 1)..];
            return last.Length > 0 && char.IsUpper(last[0]);
        }

        /// <summary>One piece of a tag after its name: whitespace, an attribute's name, its <c>=</c> or its value.</summary>
        private int TagPart(int j)
        {
            var c = s[j];
            if (char.IsWhiteSpace(c))
            {
                var k = j;
                while (k < s.Length && char.IsWhiteSpace(s[k]))
                {
                    k++;
                }
                Add(Kind.Text, s[j..k]);
                return k;
            }
            if (c == '=')
            {
                Add(Kind.Text, "=");
                _afterEquals = true;
                return j + 1;
            }
            if (c is '"' or '\'')
            {
                _afterEquals = false;
                return _valueKind == ValueKind.Markup ? AttributeValue(j) : CSharpValue(j);
            }
            if (Starts(s, j, "@*"))
            {
                return RazorComment(j);
            }
            var start = j;
            while (j < s.Length && !char.IsWhiteSpace(s[j]) && s[j] is not ('=' or '>' or '"' or '\'') &&
                   !(s[j] == '/' && j + 1 < s.Length && s[j + 1] == '>'))
            {
                j++;
            }
            if (j == start)
            {
                Add(Kind.Text, c.ToString());
                return j + 1;
            }
            if (_afterEquals)
            {
                return UnquotedValue(start, j);
            }
            AttributeName(s[start..j]);
            return j;
        }

        /// <summary>
        /// An attribute's name: a directive attribute (<c>@onclick</c>, <c>@bind-Value:after</c>), a component's parameter, or
        /// an HTML attribute (which a component takes into its splatted attributes). It decides how the value is highlighted.
        /// </summary>
        private void AttributeName(string name)
        {
            if (name.Length > 1 && name[0] == '@')
            {
                Add(Kind.Control, name);
                _valueKind = DirectiveValue(name);
                return;
            }
            var binding = _component is null ? null : ComponentAttribute(_component, name);
            Add(binding is null ? Kind.Attribute : Kind.Type, name);
            _valueKind = binding ?? ValueKind.Markup;
        }

        /// <summary>
        /// How a component attribute's value is highlighted, or null for an attribute that isn't one of its parameters. An
        /// unknown component's capitalised attributes are taken for parameters, with string values.
        /// </summary>
        private ValueKind? ComponentAttribute(string component, string name)
        {
            if (Symbols.Parameter(component, name, info) is { } binding)
            {
                return binding;
            }
            return name == "Context" || (char.IsUpper(name[0]) && !Symbols.IsComponent(component, info)) ? ValueKind.Markup : null;
        }

        /// <summary>A directive attribute's value is C#, except for the names of <c>:event</c>, <c>:format</c> and <c>@formname</c>.</summary>
        private static ValueKind DirectiveValue(string name)
        {
            var colon = name.IndexOf(':');
            var suffix = colon < 0 ? "" : name[(colon + 1)..];
            if (name == "@formname" || suffix is "event" or "format")
            {
                return ValueKind.Markup;
            }
            if (suffix is "after" or "set")
            {
                return ValueKind.Method;
            }
            return suffix.Length == 0 && name.StartsWith("@on", StringComparison.Ordinal) ? ValueKind.Method : ValueKind.CSharp;
        }

        private int UnquotedValue(int start, int end)
        {
            _afterEquals = false;
            if (_valueKind != ValueKind.Markup)
            {
                CSharpValueContent(start, end);
                return end;
            }
            if (s[start] == '@' && start + 1 < end)
            {
                return ValueTransition(start);
            }
            Add(Kind.AttributeValue, s[start..end]);
            return end;
        }

        /// <summary>A quoted attribute value: a string, with any Razor in it highlighted as C#.</summary>
        private int AttributeValue(int j)
        {
            var quote = s[j];
            Add(Kind.TagPunctuation, quote.ToString());
            var run = new StringBuilder();
            var k = j + 1;
            while (k < s.Length && s[k] != quote)
            {
                if (s[k] == '@' && k + 1 < s.Length && s[k + 1] != '@' && !char.IsLetterOrDigit(s[k - 1]))
                {
                    Add(Kind.AttributeValue, run.ToString());
                    run.Clear();
                    k = ValueTransition(k);
                    continue;
                }
                if (s[k] == '@' && k + 1 < s.Length && s[k + 1] == '@')
                {
                    run.Append("@@");
                    k += 2;
                    continue;
                }
                run.Append(s[k]);
                k++;
            }
            Add(Kind.AttributeValue, run.ToString());
            if (k < s.Length)
            {
                Add(Kind.TagPunctuation, quote.ToString());
                k++;
            }
            return k;
        }

        /// <summary>A quoted attribute value that is C#: a directive attribute's, or a component parameter's that isn't a string.</summary>
        private int CSharpValue(int j)
        {
            var quote = s[j];
            var close = j + 1;
            while (close < s.Length && s[close] != quote)
            {
                close = Starts(s, close, "@(") ? Matching(s, close + 1, '(', ')') : close + 1;
            }
            Add(Kind.TagPunctuation, quote.ToString());
            CSharpValueContent(j + 1, close);
            if (close >= s.Length)
            {
                return s.Length;
            }
            Add(Kind.TagPunctuation, quote.ToString());
            return close + 1;
        }

        /// <summary>
        /// C# in an attribute value, which may start with an <c>@</c>. The grammar takes the value for a string, so its
        /// punctuation keeps the string's colour; the semantic tokens colour its names, keywords, literals and operators.
        /// </summary>
        private void CSharpValueContent(int start, int end)
        {
            var k = start;
            if (Starts(s, k, "@(") && k + 1 < end)
            {
                k = Math.Min(end, ExplicitExpression(k, inAttribute: true));
            }
            else if (k < end && s[k] == '@')
            {
                Add(Kind.Control, "@");
                k++;
            }
            new CSharpScanner(s, tokens, info)
            {
                InAttribute = true,
                MethodGroup = _valueKind == ValueKind.Method,
                TypeContext = _valueKind == ValueKind.TypeName,
            }.Scan(k, end);
        }

        private int ValueTransition(int k)
        {
            if (s[k + 1] == '(')
            {
                return ExplicitExpression(k, inAttribute: true);
            }
            var word = WordAt(k + 1);
            if (word.Length == 0)
            {
                Add(Kind.AttributeValue, "@");
                return k + 1;
            }
            // A quote can only be inside the expression's parentheses (a string argument), so the expression ends before the value's.
            Add(Kind.Control, "@");
            var end = ImplicitEnd(k + 1 + word.Length, s.Length);
            new CSharpScanner(s, tokens, info) { InAttribute = true }.Scan(k + 1, end);
            return end;
        }

        /// <summary>A script's JavaScript or a style's CSS, up to the closing tag.</summary>
        private int EmbeddedCode(int start, string name)
        {
            var close = s.IndexOf("</" + name, start, StringComparison.OrdinalIgnoreCase);
            close = close < 0 ? s.Length : close;
            if (name == "script")
            {
                new JsScanner(s, tokens).Scan(start, close);
            }
            else
            {
                new CssScanner(s, tokens).Scan(start, close);
            }
            return close;
        }

        private void PopElement(string name)
        {
            for (var k = _frames.Count - 1; k >= 0; k--)
            {
                if (_frames[k].Code)
                {
                    return;
                }
                if (_frames[k].Element == name)
                {
                    _frames.RemoveRange(k, _frames.Count - k);
                    return;
                }
            }
        }

        /// <summary>The end of an implicit expression: member access, calls and indexers (<c>@item.Name</c>, <c>@Format(x)</c>).</summary>
        private int ImplicitEnd(int j, int limit)
        {
            var k = j;
            while (k < limit)
            {
                if (s[k] == '.' && k + 1 < limit && (char.IsLetter(s[k + 1]) || s[k + 1] == '_'))
                {
                    k++;
                    while (k < limit && (char.IsLetterOrDigit(s[k]) || s[k] == '_'))
                    {
                        k++;
                    }
                }
                else if (s[k] is '(' or '[')
                {
                    k = Math.Min(limit, Matching(s, k, s[k], s[k] == '(' ? ')' : ']'));
                }
                else
                {
                    break;
                }
            }
            return k;
        }

        /// <summary>The end of a C# statement in a code block: the end of its line, or a brace that opens or closes a block.</summary>
        private int StatementEnd(int i)
        {
            var depth = 0;
            var k = i;
            while (k < s.Length)
            {
                var c = s[k];
                if (c == '"')
                {
                    k = SkipString(s, k);
                    continue;
                }
                if (c is '(' or '[')
                {
                    depth++;
                }
                else if (c is ')' or ']')
                {
                    depth--;
                }
                else if (depth <= 0 && c is '\n' or '{' or '}')
                {
                    return k;
                }
                k++;
            }
            return k;
        }

        private string WordAt(int i)
        {
            var k = i;
            while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] == '_'))
            {
                k++;
            }
            return k > i && !char.IsDigit(s[i]) ? s[i..k] : "";
        }

        private int SkipWhitespace(int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
            return i;
        }

        private bool LineStartsAt(int i)
        {
            var k = i - 1;
            while (k >= 0 && s[k] is ' ' or '\t')
            {
                k--;
            }
            return k < 0 || s[k] == '\n';
        }
    }

    // ---- C# -------------------------------------------------------------------------------------

    private static readonly HashSet<string> CSharpKeywords =
    [
        "abstract", "and", "as", "async", "await", "break", "case", "catch", "checked", "class", "const", "continue", "default",
        "delegate", "do", "else", "enum", "event", "explicit", "extern", "finally", "fixed", "for", "foreach", "get", "goto", "if",
        "implicit", "in", "init", "interface", "internal", "is", "lock", "namespace", "new", "not", "operator", "or", "out", "override",
        "params", "partial", "private", "protected", "public", "readonly", "record", "ref", "return", "sealed", "set", "sizeof",
        "stackalloc", "static", "struct", "switch", "throw", "try", "typeof", "unchecked", "unsafe", "using", "virtual", "volatile",
        "when", "where", "while", "with", "yield", "nameof",
    ];

    private static readonly HashSet<string> CSharpControlKeywords =
    [
        "await", "break", "case", "catch", "continue", "default", "do", "else", "finally", "for", "foreach", "goto", "if", "in", "return",
        "switch", "throw", "try", "when", "while", "yield",
    ];

    private static readonly HashSet<string> CSharpTypeKeywords =
    [
        "bool", "byte", "char", "decimal", "double", "dynamic", "float", "int", "long", "nint", "nuint", "object", "sbyte", "short",
        "string", "uint", "ulong", "ushort", "var", "void",
    ];

    // Words after which an identifier can't be a type in a declaration ("x is null", "item in Items").
    private static readonly HashSet<string> CSharpOperatorWords = ["is", "as", "in", "when", "and", "or", "switch", "with", "not"];

    private static readonly string[] CSharpOperators =
    [
        ">>=", "<<=", "??=", "=>", "==", "!=", "<=", ">=", "&&", "||", "++", "--", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=",
        "<<", ">>", "..", "=", "+", "-", "*", "/", "%", "&", "|", "^", "~", "!", "<", ">",
    ];

    // The type keywords whose types have constants (int.MaxValue) or static members that qualify further.
    private static readonly Dictionary<string, Type> CSharpKeywordTypes = new()
    {
        ["bool"] = typeof(bool), ["byte"] = typeof(byte), ["char"] = typeof(char), ["decimal"] = typeof(decimal),
        ["double"] = typeof(double), ["float"] = typeof(float), ["int"] = typeof(int), ["long"] = typeof(long),
        ["object"] = typeof(object), ["short"] = typeof(short), ["string"] = typeof(string), ["uint"] = typeof(uint),
        ["ulong"] = typeof(ulong),
    };

    /// <summary>
    /// C#, as VS Code shows it with the C# extension: the C# TextMate grammar's scopes (keywords, type names, declared names,
    /// method names, strings with their escapes and interpolations), overlaid with what Roslyn's semantic tokens change.
    /// Those come from reflection over the loaded assemblies and from the snippet's own declarations: types and namespaces
    /// that qualify a member, enum members and constants, method groups, operators (member access's <c>.</c> included) and
    /// XML doc comments in the comment colour.
    /// </summary>
    private sealed class CSharpScanner(string s, List<(Kind Kind, string Text)> tokens, SnippetInfo info)
    {
        // Tokens that end a type (a type name, a type keyword, a generic's '>', a nullable '?' or '[]'), so the next word is declared.
        private readonly HashSet<int> _typeEnds = [];
        private int _ternaries;
        private int _parens;
        private int _genericEnd = -1;
        private bool _genericOwnerIsType;
        private int _namespaceEnd = -1;
        private int _qualifiedTypeEnd = -1;
        private int _attributeEnd = -1;
        private int _attributeParens;
        private bool _attributeName;

        /// <summary>The last name that qualifies a member access (a type, namespace or enum), and the one the word being classified makes.</summary>
        private Qualifier? _qualifier;
        private Qualifier? _nextQualifier;

        /// <summary>Inside a string's interpolation, whose punctuation takes the string's colour.</summary>
        private bool Interpolation { get; init; }

        /// <summary>
        /// In a markup attribute's value: the Razor grammar takes it for a string, so its punctuation keeps the string's colour.
        /// </summary>
        public bool InAttribute { get; init; }

        /// <summary>A delegate's value (an event callback): a lone name is a method group.</summary>
        public bool MethodGroup { get; init; }

        /// <summary>A type argument's value (a generic component's <c>TItem</c>): every name is a type.</summary>
        public bool TypeContext { get; init; }

        public void Scan(int start, int end)
        {
            if (TypeContext)
            {
                _genericEnd = end;
            }
            var i = start;
            while (i < end)
            {
                i = Step(i, end);
            }
        }

        private int Add(Kind kind, string text)
        {
            tokens.Add((kind, text));
            return tokens.Count - 1;
        }

        private int AddPunctuation(string text)
        {
            if (Interpolation)
            {
                return Add(Kind.String, text);
            }
            return Add(InAttribute ? Kind.AttributeValue : Kind.Text, text);
        }

        private int Step(int i, int end)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
            {
                var k = i;
                while (k < end && char.IsWhiteSpace(s[k]))
                {
                    k++;
                }
                Add(Kind.Text, s[i..k]);
                return k;
            }
            if (Starts(s, i, "//"))
            {
                var lineEnd = LineEnd(s, i, end);
                Add(Kind.Comment, s[i..lineEnd]);
                return lineEnd;
            }
            if (Starts(s, i, "/*"))
            {
                var close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                close = close < 0 || close + 2 > end ? end : close + 2;
                Add(Kind.Comment, s[i..close]);
                return close;
            }
            if (IsStringStart(i, end))
            {
                return StringLiteral(i, end);
            }
            if (c == '\'')
            {
                return CharLiteral(i, end);
            }
            if (char.IsDigit(c))
            {
                return Number(i, end);
            }
            if (char.IsLetter(c) || c == '_' || (c == '@' && i + 1 < end && char.IsLetter(s[i + 1])))
            {
                return Word(i, end);
            }
            return Symbol(i, end);
        }

        private int Word(int i, int end)
        {
            var e = s[i] == '@' ? i + 1 : i;
            while (e < end && (char.IsLetterOrDigit(s[e]) || s[e] == '_'))
            {
                e++;
            }
            var word = s[i..e];
            var genericClose = i >= _genericEnd && e < end && s[e] == '<' ? GenericEnd(e, end) : -1;
            var kind = Semantic(word, i, e, end, Classify(word, i, e, genericClose, end));
            var index = Add(kind, word);
            if (_nextQualifier is { } qualifier)
            {
                _qualifier = qualifier with { Token = index };
            }
            if (kind == Kind.Type || (kind == Kind.Keyword && CSharpTypeKeywords.Contains(word)))
            {
                _typeEnds.Add(index);
            }
            if (genericClose > 0)
            {
                _genericEnd = genericClose;
                _genericOwnerIsType = kind == Kind.Type;
            }
            if (word == "using" && IsUsingDirective(i, end))
            {
                _namespaceEnd = s.IndexOf(';', e);
            }
            else if (word == "namespace")
            {
                _namespaceEnd = LineEnd(s, e, end);
            }
            return e;
        }

        private Kind Classify(string word, int i, int e, int genericClose, int end)
        {
            if (i < _genericEnd)
            {
                return CSharpKeywords.Contains(word) || CSharpTypeKeywords.Contains(word) ? Kind.Keyword : Kind.Type;
            }
            if (i < _namespaceEnd)
            {
                return word == "static" ? Kind.Keyword : Kind.Type;
            }
            if (_attributeName && i < _attributeEnd)
            {
                _attributeName = false;
                return Kind.Type;
            }
            switch (word)
            {
                case "true" or "false":
                    return Kind.Constant;
                case "null" or "this" or "base":
                    return Kind.Constant;
                case "_" when Starts(s, SkipSpaces(s, e, end), "=>"):
                    // A switch arm's discard; "_ =>" as a lambda's parameter is a declaration.
                    return Previous() is "(" or "@(" or "=" ? Kind.Variable : Kind.Constant;
            }
            if (CSharpKeywords.Contains(word) || CSharpTypeKeywords.Contains(word))
            {
                return CSharpControlKeywords.Contains(word) ? Kind.Control : Kind.Keyword;
            }
            if (word == "required" && IsIdentifierStart(SkipSpaces(s, e, end), end))
            {
                return Kind.Keyword;
            }

            var previous = Previous();
            if (i < _qualifiedTypeEnd)
            {
                return Kind.Type;
            }
            if (previous is "new" or "class" or "struct" or "record" or "interface" or "enum" or "is" or "as" or "or" or "and" or "not" or "case" ||
                (previous == ":" && IsBaseList()) || (previous == "(" && TokenBefore(PreviousIndex()) is "typeof" or "sizeof" or "default" or "catch") ||
                IsSwitchArm(e, end))
            {
                return Kind.Type;
            }
            if (previous == "}" && IsOnPreviousTokensLine())
            {
                // A property pattern's designation: "is { } start".
                return Kind.Variable;
            }
            if (e < end && s[e] == '.' && QualifiedTypeEnd(e, end) is var qualifiedEnd and > 0)
            {
                // A namespace-qualified type in a declaration: "System.Text.RegularExpressions.Regex AnsiCode".
                _qualifiedTypeEnd = qualifiedEnd;
                return Kind.Type;
            }
            var after = genericClose > 0 ? genericClose : e;
            var next = SkipSpaces(s, after, end);
            if (next < end && s[next] == '(')
            {
                return Kind.Function;
            }
            if (genericClose > 0)
            {
                return Kind.Type;
            }
            var followed = e < end ? s[e] : '\0';
            if (followed != '.' && PreviousIsTypeEnd())
            {
                return Kind.Variable;
            }
            if (Starts(s, next, "=>"))
            {
                return Kind.Variable;
            }
            if (next > e && IsIdentifierStart(next, end) && !CSharpOperatorWords.Contains(WordFrom(next, end)))
            {
                return Kind.Type;
            }
            if (followed == '?' && e + 1 < end && (s[e + 1] is '>' or ',' or ')' or '[' or ']' || (s[e + 1] == ' ' && IsIdentifierStart(SkipSpaces(s, e + 1, end), end) &&
                                                                                                    !CSharpOperatorWords.Contains(WordFrom(SkipSpaces(s, e + 1, end), end)))))
            {
                return Kind.Type;
            }
            if (followed == '[' && e + 1 < end && s[e + 1] == ']')
            {
                return Kind.Type;
            }
            // A variable, a property or a parameter, a named argument's name (name: value) and an attribute's (Name = value).
            return Kind.Variable;
        }

        /// <summary>
        /// The colour Roslyn's semantic tokens give a name that the grammar's rules took for a variable, type or method: a type
        /// or namespace that qualifies a member, a member of one (enum member, constant, method, nested type), a constant or
        /// enum member the snippet declares, or a method group.
        /// </summary>
        private Kind Semantic(string word, int i, int e, int end, Kind kind)
        {
            _nextQualifier = null;
            var dotted = e + 1 < end && s[e] == '.' && s[e + 1] != '.';
            if (kind == Kind.Keyword)
            {
                if (dotted && CSharpKeywordTypes.TryGetValue(word, out var keywordType))
                {
                    _nextQualifier = new Qualifier(keywordType);
                }
                return kind;
            }
            if (kind is not (Kind.Variable or Kind.Type or Kind.Function))
            {
                return kind;
            }
            var previous = PreviousIndex();
            if (previous >= 0 && tokens[previous].Text == "." && QualifierBefore(previous) is { } qualifier)
            {
                return Member(qualifier, word, kind, dotted);
            }
            if (kind != Kind.Function && (info.Constants.Contains(word) || info.InEnumBody(i)))
            {
                return Kind.ConstantVariable;
            }
            if (dotted && kind != Kind.Function && Qualify(word) is { } qualifies &&
                (kind == Kind.Type || !info.Variables.Contains(word) || (info.ColorColor.Contains(word) && qualifies.HasStatic(WordFrom(e + 1, end)))))
            {
                _nextQualifier = qualifies;
                return Kind.Type;
            }
            var methodGroup = info.Methods.Contains(word) || (MethodGroup && SkipSpaces(s, e, end) >= end);
            return kind == Kind.Variable && methodGroup && !PreviousIsTypeEnd() ? Kind.Function : kind;
        }

        /// <summary>The qualifier of the member access whose <c>.</c> is the token at <paramref name="dot"/>, if it's known.</summary>
        private Qualifier? QualifierBefore(int dot)
        {
            var k = dot - 1;
            while (k >= 0 && string.IsNullOrWhiteSpace(tokens[k].Text))
            {
                k--;
            }
            return _qualifier is { } qualifier && qualifier.Token == k ? qualifier : null;
        }

        /// <summary>What a name that starts a member access is: a snippet's enum or type, a known type, or a namespace.</summary>
        private Qualifier? Qualify(string word)
        {
            if (info.Enums.TryGetValue(word, out var members))
            {
                return new Qualifier(Members: members);
            }
            if (Symbols.FindType(word, info) is { } type)
            {
                return new Qualifier(type);
            }
            if (info.Types.Contains(word))
            {
                return new Qualifier();
            }
            return Symbols.IsNamespace(word) ? new Qualifier(Namespace: word) : null;
        }

        private Kind Member(Qualifier qualifier, string word, Kind kind, bool dotted)
        {
            if (qualifier.Members is { } members)
            {
                return members.Contains(word) ? Kind.ConstantVariable : kind;
            }
            if (qualifier.Namespace is { } ns)
            {
                var name = ns + "." + word;
                if (Symbols.IsNamespace(name))
                {
                    _nextQualifier = new Qualifier(Namespace: name);
                    return Kind.Type;
                }
                if (Symbols.FindQualifiedType(name) is { } type)
                {
                    _nextQualifier = new Qualifier(type);
                    return Kind.Type;
                }
                return kind;
            }
            if (qualifier.Type is null)
            {
                return kind;
            }
            var (memberKind, nested) = Symbols.Member(qualifier.Type, word);
            if (dotted && nested is not null)
            {
                _nextQualifier = new Qualifier(nested);
            }
            return memberKind ?? kind;
        }

        /// <summary>A switch expression's arm that starts its line with a qualified constant: "BubbleVariant.Muted =&gt; ...".</summary>
        private bool IsSwitchArm(int e, int end)
        {
            if (e >= end || s[e] != '.' || tokens.Count == 0 || !tokens[^1].Text.Contains('\n'))
            {
                return false;
            }
            var k = e;
            while (k < end && (s[k] == '.' || char.IsLetterOrDigit(s[k]) || s[k] == '_'))
            {
                k++;
            }
            var next = SkipSpaces(s, k, end);
            return Starts(s, next, "=>") || WordFrom(next, end) == "when";
        }

        private bool IsOnPreviousTokensLine() => tokens.Count > 0 && !tokens[^1].Text.Contains('\n');

        /// <summary>
        /// The end of a dotted name at <paramref name="dot"/> that is a declaration's type (followed by the declared name), or -1.
        /// </summary>
        private int QualifiedTypeEnd(int dot, int end)
        {
            var k = dot;
            while (k < end && s[k] == '.' && k + 1 < end && (char.IsLetter(s[k + 1]) || s[k + 1] == '_'))
            {
                k++;
                while (k < end && (char.IsLetterOrDigit(s[k]) || s[k] == '_'))
                {
                    k++;
                }
            }
            if (k == dot)
            {
                return -1;
            }
            var after = k < end && s[k] == '?' ? k + 1 : k;
            var next = SkipSpaces(s, after, end);
            return next > after && IsIdentifierStart(next, end) && !CSharpOperatorWords.Contains(WordFrom(next, end)) ? k : -1;
        }

        private string TokenBefore(int index)
        {
            for (var k = index - 1; k >= 0; k--)
            {
                if (!string.IsNullOrWhiteSpace(tokens[k].Text))
                {
                    return tokens[k].Text;
                }
            }
            return "";
        }

        private bool IsBaseList()
        {
            // "class Foo : Bar" or "record Foo(...) : Bar": the colon after a type declaration's name.
            var colon = PreviousIndex();
            for (var k = colon - 1; k >= 0; k--)
            {
                var text = tokens[k].Text;
                if (text is "class" or "struct" or "record" or "interface")
                {
                    return true;
                }
                if (text.Contains('\n') || text is ";" or "{" or "}" or "?")
                {
                    return false;
                }
            }
            return false;
        }

        private bool IsUsingDirective(int i, int end)
        {
            var lineEnd = LineEnd(s, i, end);
            return UsingDirectiveLine().IsMatch(s[i..lineEnd]);
        }

        private int Symbol(int i, int end)
        {
            var c = s[i];
            if (i < _genericEnd)
            {
                var index = c == '.' ? Add(Kind.Operator, ".") : AddPunctuation(c.ToString());
                if (c == '>' && i == _genericEnd - 1 && _genericOwnerIsType)
                {
                    _typeEnds.Add(index);
                }
                return i + 1;
            }
            switch (c)
            {
                case '[':
                    return OpenBracket(i, end);
                case ']':
                    if (i == _attributeEnd - 1)
                    {
                        _attributeEnd = -1;
                    }
                    AddPunctuation("]");
                    return i + 1;
                case '(':
                    _parens++;
                    if (i < _attributeEnd)
                    {
                        _attributeParens++;
                    }
                    AddPunctuation("(");
                    return i + 1;
                case ')':
                    _parens--;
                    if (i < _attributeEnd)
                    {
                        _attributeParens--;
                    }
                    AddPunctuation(")");
                    return i + 1;
                case ',':
                    if (i < _attributeEnd && _attributeParens == 0)
                    {
                        _attributeName = true;
                    }
                    AddPunctuation(",");
                    return i + 1;
                case ';' or '{' or '}':
                    _ternaries = 0;
                    AddPunctuation(c.ToString());
                    return i + 1;
                case '?':
                    return Question(i, end);
                case ':':
                    return Colon(i);
                case '.' when !Starts(s, i, ".."):
                    Add(Kind.Operator, ".");
                    return i + 1;
            }
            foreach (var op in CSharpOperators)
            {
                if (Starts(s, i, op) && i + op.Length <= end)
                {
                    // A range's ".." is punctuation to Roslyn; the rest are operators.
                    if (op == "..")
                    {
                        AddPunctuation(op);
                    }
                    else
                    {
                        Add(Kind.Operator, op);
                    }
                    return i + op.Length;
                }
            }
            AddPunctuation(c.ToString());
            return i + 1;
        }

        private int OpenBracket(int i, int end)
        {
            if (i + 1 < end && s[i + 1] == ']')
            {
                var typed = PreviousIsTypeEnd();
                var index = AddPunctuation("[]");
                if (typed)
                {
                    _typeEnds.Add(index);
                }
                return i + 2;
            }
            if (IsAttributeStart(i, end))
            {
                _attributeEnd = Math.Min(end, Matching(s, i, '[', ']'));
                _attributeParens = 0;
                _attributeName = true;
            }
            AddPunctuation("[");
            return i + 1;
        }

        /// <summary>An attribute section: <c>[Name, Name(...)]</c> that starts its line (or follows another one).</summary>
        private bool IsAttributeStart(int i, int end)
        {
            var previous = Previous();
            var k = i - 1;
            while (k >= 0 && s[k] is ' ' or '\t')
            {
                k--;
            }
            var lineStart = k < 0 || s[k] == '\n';
            if (!lineStart && previous is not ("]" or "@attribute"))
            {
                return false;
            }
            var name = SkipSpaces(s, i + 1, end);
            var word = WordFrom(name, end);
            if (word.Length == 0 || (char.IsLower(word[0]) && word is not ("return" or "assembly" or "module" or "field" or "property" or "method" or "type" or "param")))
            {
                return false;
            }
            var after = SkipSpaces(s, name + word.Length, end);
            return after < end && s[after] is '(' or ',' or ']' or ':' or '.' or '<';
        }

        private int Question(int i, int end)
        {
            if (Starts(s, i, "??="))
            {
                Add(Kind.Operator, "??=");
                return i + 3;
            }
            if (Starts(s, i, "??"))
            {
                Add(Kind.Operator, "??");
                return i + 2;
            }
            if (i + 1 < end && s[i + 1] is '.' or '[')
            {
                Add(Kind.Operator, "?");
                return i + 1;
            }
            if (i > 0 && !char.IsWhiteSpace(s[i - 1]) && PreviousIsTypeEnd())
            {
                _typeEnds.Add(Add(Kind.Operator, "?"));
                return i + 1;
            }
            _ternaries++;
            Add(Kind.Operator, "?");
            return i + 1;
        }

        private int Colon(int i)
        {
            if (Starts(s, i, "::"))
            {
                Add(Kind.Operator, "::");
                return i + 2;
            }
            if (_ternaries > 0)
            {
                _ternaries--;
                Add(Kind.Operator, ":");
                return i + 1;
            }
            if (Interpolation && _parens == 0)
            {
                // The grammar has no format clause ({value:N2}): it reads the colon as an operator; Roslyn classifies the
                // format as a string.
                Add(Kind.Operator, ":");
                var close = s.IndexOf('}', i + 1);
                if (close <= i + 1)
                {
                    return i + 1;
                }
                Add(Kind.String, s[(i + 1)..close]);
                return close;
            }
            AddPunctuation(":");
            return i + 1;
        }

        /// <summary>The index after the <c>&gt;</c> that closes a generic argument list at <paramref name="lt"/>, or -1.</summary>
        private int GenericEnd(int lt, int end)
        {
            var depth = 0;
            var k = lt;
            while (k < end)
            {
                var c = s[k];
                if (c == '<')
                {
                    depth++;
                }
                else if (c == '>')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return k + 1;
                    }
                }
                else if (!(char.IsLetterOrDigit(c) || c is '_' or '.' or ',' or ' ' or '?' or '[' or ']' or '(' or ')'))
                {
                    return -1;
                }
                k++;
            }
            return -1;
        }

        private bool IsStringStart(int i, int end)
        {
            var k = i;
            while (k < end && k < i + 3 && s[k] is '$' or '@')
            {
                k++;
            }
            return k < end && s[k] == '"' && (k == i || s[i] is '$' or '@');
        }

        private int StringLiteral(int i, int end)
        {
            var k = i;
            var interpolated = false;
            var verbatim = false;
            while (s[k] is '$' or '@')
            {
                interpolated |= s[k] == '$';
                verbatim |= s[k] == '@';
                k++;
            }
            if (Starts(s, k, "\"\"\""))
            {
                var quotes = 0;
                while (k + quotes < end && s[k + quotes] == '"')
                {
                    quotes++;
                }
                var close = s.IndexOf(new string('"', quotes), k + quotes, StringComparison.Ordinal);
                var stop = close < 0 || close + quotes > end ? end : close + quotes;
                Add(Kind.String, s[i..stop]);
                return stop;
            }

            var run = new StringBuilder(s[i..(k + 1)]);
            k++;
            void Flush()
            {
                if (run.Length > 0)
                {
                    Add(Kind.String, run.ToString());
                    run.Clear();
                }
            }
            while (k < end)
            {
                var c = s[k];
                if (c == '"')
                {
                    if (verbatim && k + 1 < end && s[k + 1] == '"')
                    {
                        Flush();
                        Add(Kind.Escape, "\"\"");
                        k += 2;
                        continue;
                    }
                    run.Append('"');
                    k++;
                    break;
                }
                if (c == '\n' && !verbatim)
                {
                    break;
                }
                if (c == '\\' && !verbatim)
                {
                    Flush();
                    var length = EscapeLength(k, end);
                    Add(Kind.Escape, s.Substring(k, length));
                    k += length;
                    continue;
                }
                if (interpolated && c is '{' or '}' && k + 1 < end && s[k + 1] == c)
                {
                    Flush();
                    Add(Kind.Escape, s.Substring(k, 2));
                    k += 2;
                    continue;
                }
                if (interpolated && c == '{')
                {
                    Flush();
                    Add(Kind.String, "{");
                    var close = InterpolationEnd(k, end);
                    new CSharpScanner(s, tokens, info) { Interpolation = true }.Scan(k + 1, close);
                    if (close < end)
                    {
                        Add(Kind.String, "}");
                    }
                    k = close + 1;
                    continue;
                }
                run.Append(c);
                k++;
            }
            Flush();
            return Math.Min(k, end);
        }

        private int InterpolationEnd(int open, int end)
        {
            var depth = 0;
            var k = open;
            while (k < end)
            {
                var c = s[k];
                if (c == '"')
                {
                    k = SkipString(s, k);
                    continue;
                }
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return k;
                    }
                }
                k++;
            }
            return end;
        }

        private int EscapeLength(int k, int end)
        {
            if (k + 1 >= end)
            {
                return 1;
            }
            var length = s[k + 1] switch
            {
                'u' => 6,
                'U' => 10,
                'x' => 2 + HexDigits(k + 2, end, 4),
                _ => 2,
            };
            return Math.Min(length, end - k);
        }

        private int HexDigits(int k, int end, int max)
        {
            var n = 0;
            while (n < max && k + n < end && Uri.IsHexDigit(s[k + n]))
            {
                n++;
            }
            return n;
        }

        private int CharLiteral(int i, int end)
        {
            var k = i + 1;
            var escape = k < end && s[k] == '\\';
            var contentEnd = escape ? k + EscapeLength(k, end) : k + 1;
            if (contentEnd >= end || s[contentEnd] != '\'')
            {
                AddPunctuation("'");
                return i + 1;
            }
            Add(Kind.String, "'");
            Add(escape ? Kind.Escape : Kind.String, s[k..contentEnd]);
            Add(Kind.String, "'");
            return contentEnd + 1;
        }

        private int Number(int i, int end)
        {
            var k = i;
            while (k < end && (char.IsLetterOrDigit(s[k]) || s[k] == '_' || (s[k] == '.' && k + 1 < end && char.IsDigit(s[k + 1]))))
            {
                k++;
            }
            Add(Kind.Number, s[i..k]);
            return k;
        }

        private int PreviousIndex()
        {
            for (var k = tokens.Count - 1; k >= 0; k--)
            {
                if (!string.IsNullOrWhiteSpace(tokens[k].Text))
                {
                    return k;
                }
            }
            return -1;
        }

        private string Previous()
        {
            var k = PreviousIndex();
            return k < 0 ? "" : tokens[k].Text;
        }

        private bool PreviousIsTypeEnd() => _typeEnds.Contains(PreviousIndex());

        private bool IsIdentifierStart(int k, int end) => k < end && (char.IsLetter(s[k]) || s[k] is '_' or '@');

        private string WordFrom(int k, int end)
        {
            var e = k;
            while (e < end && (char.IsLetterOrDigit(s[e]) || s[e] == '_'))
            {
                e++;
            }
            return s[k..e];
        }
    }

    // ---- JavaScript -----------------------------------------------------------------------------

    private static readonly HashSet<string> JsKeywords =
    [
        "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else",
        "export", "extends", "finally", "for", "function", "if", "import", "in", "instanceof", "let", "new", "return", "static",
        "switch", "throw", "try", "typeof", "var", "void", "while", "with", "yield",
    ];

    private static readonly HashSet<string> JsControlKeywords =
    [
        "await", "break", "case", "catch", "continue", "default", "do", "else", "export", "finally", "for", "if", "import", "return",
        "switch", "throw", "try", "while", "with", "yield",
    ];

    private static readonly string[] JsOperators =
    [
        ">>>=", "===", "!==", "**=", "...", ">>>", "&&=", "||=", "??=", ">>=", "<<=", "=>", "==", "!=", "<=", ">=", "&&", "||", "??", "**",
        "++", "--", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<", ">>", "=", "+", "-", "*", "/", "%", "&", "|", "^", "~", "!",
        "<", ">",
    ];

    /// <summary>JavaScript, as Shiki's grammar scopes it: keywords and operators, functions, parameters, constants and strings.</summary>
    private sealed class JsScanner(string s, List<(Kind Kind, string Text)> tokens)
    {
        private int _ternaries;
        private int _parens;
        private bool _constDeclaration;
        private bool _import;
        private int _importBraces;
        private int _parameterDepth = -1;
        private readonly HashSet<int> _docTokens = [];

        public void Scan(int start, int end)
        {
            var i = start;
            while (i < end)
            {
                i = Step(i, end);
            }
        }

        private void Add(Kind kind, string text) => tokens.Add((kind, text));

        private int Step(int i, int end)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
            {
                var k = i;
                while (k < end && char.IsWhiteSpace(s[k]))
                {
                    k++;
                }
                var whitespace = s[i..k];
                if (_importBraces == 0 && whitespace.Contains('\n'))
                {
                    // An import without a semicolon ends with its line.
                    _import = false;
                }
                Add(Kind.Text, whitespace);
                return k;
            }
            if (Starts(s, i, "//"))
            {
                var lineEnd = LineEnd(s, i, end);
                Add(Kind.Comment, s[i..lineEnd]);
                return lineEnd;
            }
            if (Starts(s, i, "/*"))
            {
                var close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                close = close < 0 || close + 2 > end ? end : close + 2;
                if (Starts(s, i, "/**"))
                {
                    JsDoc(i, close);
                }
                else
                {
                    Add(Kind.Comment, s[i..close]);
                }
                return close;
            }
            if (c is '"' or '\'')
            {
                return Quoted(i, end);
            }
            if (c == '`')
            {
                return Template(i, end);
            }
            if (c == '/' && RegexAllowed())
            {
                return RegexLiteral(i, end);
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < end && char.IsDigit(s[i + 1])))
            {
                var k = i;
                while (k < end && (char.IsLetterOrDigit(s[k]) || s[k] is '_' or '.'))
                {
                    k++;
                }
                Add(Kind.Number, s[i..k]);
                return k;
            }
            if (char.IsLetter(c) || c is '_' or '$')
            {
                return Word(i, end);
            }
            return Symbol(i, end);
        }

        private int Word(int i, int end)
        {
            var e = i;
            while (e < end && (char.IsLetterOrDigit(s[e]) || s[e] is '_' or '$'))
            {
                e++;
            }
            var word = s[i..e];
            var previous = Previous();
            var next = SkipSpaces(s, e, end);
            var call = next < end && (s[next] == '(' || Starts(s, next, "?.("));
            Kind kind;
            if (previous is "." or "?.")
            {
                if (call)
                {
                    kind = Kind.Function;
                }
                else
                {
                    kind = IsUpperCase(word) ? Kind.ConstantVariable : Kind.Variable;
                }
            }
            else if (word is "from" or "as" && _import)
            {
                kind = Kind.Control;
            }
            else if (word == "of" && _parens > 0 && previous is not ("(" or ","))
            {
                kind = Kind.Keyword;
                _constDeclaration = false;
            }
            else if (word is "true" or "false" or "null" or "undefined" or "this" or "super" or "Infinity" or "NaN")
            {
                kind = Kind.Constant;
            }
            else if (JsKeywords.Contains(word))
            {
                kind = JsControlKeywords.Contains(word) ? Kind.Control : Kind.Keyword;
                if (word == "const")
                {
                    _constDeclaration = true;
                }
                else if (word == "in")
                {
                    _constDeclaration = false;
                }
                else if (word is "import" or "export")
                {
                    _import = true;
                }
            }
            else if (previous is "class" or "extends" or "instanceof" || (previous == "new" && !call) || word == "Promise")
            {
                kind = Kind.Type;
            }
            else if (previous == "function" || call || (previous is "{" or "," && IsFunctionValue(next, ':', end)) ||
                     (_constDeclaration && IsFunctionValue(next, '=', end)))
            {
                kind = Kind.Function;
            }
            else if (Starts(s, next, "=>") || (_parameterDepth >= 0 && _parens > _parameterDepth && previous is "(" or "," or "{" or "..."))
            {
                kind = Kind.Variable;
            }
            else
            {
                kind = _constDeclaration || IsUpperCase(word) ? Kind.ConstantVariable : Kind.Variable;
            }
            Add(kind, word);
            return e;
        }

        private static bool IsUpperCase(string word) => word.Length > 1 && word.All(ch => char.IsUpper(ch) || ch == '_' || char.IsDigit(ch));

        /// <summary>
        /// A name whose value (after <paramref name="separator"/>) is a function: an object literal's <c>get: () =&gt; value</c>,
        /// or <c>const run = function () {}</c>.
        /// </summary>
        private bool IsFunctionValue(int at, char separator, int end)
        {
            if (at >= end || s[at] != separator || (at + 1 < end && s[at + 1] is '=' or '>'))
            {
                return false;
            }
            var value = SkipSpaces(s, at + 1, end);
            if (Starts(s, value, "function") || Starts(s, value, "async"))
            {
                return true;
            }
            var arrow = value;
            if (value < end && s[value] == '(')
            {
                arrow = Matching(s, value, '(', ')');
            }
            else
            {
                while (arrow < end && (char.IsLetterOrDigit(s[arrow]) || s[arrow] is '_' or '$'))
                {
                    arrow++;
                }
            }
            return arrow > value && Starts(s, SkipSpaces(s, arrow, end), "=>");
        }

        /// <summary>A <c>/** ... */</c> comment: its tags, <c>{types}</c> and parameter names in their own colours.</summary>
        private void JsDoc(int start, int end)
        {
            var first = tokens.Count;
            JsDocTokens(start, end);
            for (var k = first; k < tokens.Count; k++)
            {
                _docTokens.Add(k);
            }
        }

        private void JsDocTokens(int start, int end)
        {
            var text = new StringBuilder();
            void Flush()
            {
                if (text.Length > 0)
                {
                    Add(Kind.Comment, text.ToString());
                    text.Clear();
                }
            }
            var k = start;
            var nameNext = false;
            while (k < end)
            {
                var c = s[k];
                if (c == '@' && k + 1 < end && char.IsLetter(s[k + 1]))
                {
                    Flush();
                    var e = k + 1;
                    while (e < end && char.IsLetter(s[e]))
                    {
                        e++;
                    }
                    Add(Kind.Keyword, s[k..e]);
                    nameNext = s[(k + 1)..e] is "param" or "property" or "typedef" or "callback";
                    k = e;
                    continue;
                }
                if (c == '{')
                {
                    Flush();
                    var close = Math.Min(end, Matching(s, k, '{', '}'));
                    Add(Kind.Type, s[k..close]);
                    k = close;
                    continue;
                }
                if (nameNext && (char.IsLetter(c) || c is '_' or '$' or '['))
                {
                    Flush();
                    var e = k;
                    while (e < end && (char.IsLetterOrDigit(s[e]) || s[e] is '_' or '$' or '.' or '[' or ']' or '='))
                    {
                        e++;
                    }
                    Add(Kind.Variable, s[k..e]);
                    nameNext = false;
                    k = e;
                    continue;
                }
                if (!char.IsWhiteSpace(c) && c != '*')
                {
                    nameNext = false;
                }
                text.Append(c);
                k++;
            }
            Flush();
        }

        private int Symbol(int i, int end)
        {
            var c = s[i];
            switch (c)
            {
                case '(':
                    if (Previous() == "function" || (LastSignificantKind() == Kind.Function && (TokenBeforePrevious() == "function" || IsDefinition(i, end))))
                    {
                        _parameterDepth = _parens;
                    }
                    _parens++;
                    Add(Kind.Text, "(");
                    return i + 1;
                case ')':
                    _parens--;
                    if (_parens == _parameterDepth)
                    {
                        _parameterDepth = -1;
                    }
                    Add(Kind.Text, ")");
                    return i + 1;
                case ';' or '{' or '}':
                    _ternaries = 0;
                    if (c == ';')
                    {
                        _constDeclaration = false;
                        _import = false;
                    }
                    else if (_import)
                    {
                        _importBraces += c == '{' ? 1 : -1;
                    }
                    Add(Kind.Text, c.ToString());
                    return i + 1;
                case '?' when Starts(s, i, "?.") && !(i + 2 < end && char.IsDigit(s[i + 2])):
                    Add(Kind.Text, "?.");
                    return i + 2;
                case '?' when !Starts(s, i, "??"):
                    _ternaries++;
                    Add(Kind.Text, "?");
                    return i + 1;
                case ':':
                    if (_ternaries > 0)
                    {
                        _ternaries--;
                        Add(Kind.Text, ":");
                    }
                    else
                    {
                        // An object literal's key: the grammar scopes its colon with it.
                        Add(TokenBeforePrevious() is "{" or "," && LastSignificantKind() is not (Kind.Text or Kind.Keyword or Kind.Control) ? Kind.Variable : Kind.Text, ":");
                    }
                    return i + 1;
                case '.' when !Starts(s, i, "..."):
                    Add(Kind.Text, ".");
                    return i + 1;
            }
            foreach (var op in JsOperators)
            {
                if (Starts(s, i, op) && i + op.Length <= end)
                {
                    if (op == "=>")
                    {
                        ArrowParameters();
                    }
                    if (op == "=")
                    {
                        _constDeclaration = false;
                    }
                    Add(op == "=>" ? Kind.Keyword : Kind.Text, op);
                    return i + op.Length;
                }
            }
            Add(Kind.Text, c.ToString());
            return i + 1;
        }

        /// <summary>A method definition's parameter list: <c>name(a, b) {</c>, where the parentheses are followed by a block.</summary>
        private bool IsDefinition(int open, int end)
        {
            var close = Matching(s, open, '(', ')');
            var next = close;
            while (next < end && char.IsWhiteSpace(s[next]))
            {
                next++;
            }
            return close <= end && next < end && s[next] == '{';
        }

        private int Quoted(int i, int end)
        {
            var quote = s[i];
            var run = new StringBuilder().Append(quote);
            var k = i + 1;
            while (k < end && s[k] != quote && s[k] != '\n')
            {
                if (s[k] == '\\' && k + 1 < end)
                {
                    Add(Kind.String, run.ToString());
                    run.Clear();
                    Add(Kind.Escape, s.Substring(k, 2));
                    k += 2;
                    continue;
                }
                run.Append(s[k]);
                k++;
            }
            if (k < end && s[k] == quote)
            {
                run.Append(quote);
                k++;
            }
            Add(Kind.String, run.ToString());
            return k;
        }

        private int Template(int i, int end)
        {
            var run = new StringBuilder("`");
            var k = i + 1;
            while (k < end && s[k] != '`')
            {
                if (s[k] == '\\' && k + 1 < end)
                {
                    Add(Kind.String, run.ToString());
                    run.Clear();
                    Add(Kind.Escape, s.Substring(k, 2));
                    k += 2;
                    continue;
                }
                if (Starts(s, k, "${"))
                {
                    Add(Kind.String, run.ToString());
                    run.Clear();
                    Add(Kind.Keyword, "${");
                    var close = Math.Min(end, Matching(s, k + 1, '{', '}'));
                    new JsScanner(s, tokens).Scan(k + 2, Math.Max(k + 2, close - 1));
                    Add(Kind.Keyword, "}");
                    k = close;
                    continue;
                }
                run.Append(s[k]);
                k++;
            }
            if (k < end)
            {
                run.Append('`');
                k++;
            }
            Add(Kind.String, run.ToString());
            return k;
        }

        private bool RegexAllowed()
        {
            var previous = Previous();
            return previous.Length == 0 || previous is "(" or "," or "=" or ":" or "[" or "!" or "&&" or "||" or "?" or "{" or "}" or ";"
                or "return" or "typeof" or "=>";
        }

        private int RegexLiteral(int i, int end)
        {
            var k = i + 1;
            var inClass = false;
            while (k < end && s[k] != '\n')
            {
                var c = s[k];
                if (c == '\\')
                {
                    k += 2;
                    continue;
                }
                if (c == '[')
                {
                    inClass = true;
                }
                else if (c == ']')
                {
                    inClass = false;
                }
                else if (c == '/' && !inClass)
                {
                    break;
                }
                k++;
            }
            k = Math.Min(end, k + 1);
            while (k < end && char.IsLetter(s[k]))
            {
                k++;
            }
            Add(Kind.Regex, s[i..k]);
            return k;
        }

        /// <summary>At <c>=&gt;</c> after <c>(a, { b })</c>: the names in the parentheses are the arrow function's parameters.</summary>
        private void ArrowParameters()
        {
            var close = LastSignificantIndex();
            if (close < 0 || tokens[close].Text != ")")
            {
                return;
            }
            var depth = 0;
            for (var k = close; k >= 0; k--)
            {
                var text = tokens[k].Text;
                if (text == ")")
                {
                    depth++;
                }
                else if (text == "(")
                {
                    depth--;
                    if (depth == 0)
                    {
                        return;
                    }
                }
                else if (depth == 1 && tokens[k].Kind == Kind.ConstantVariable)
                {
                    tokens[k] = (Kind.Variable, text);
                }
            }
        }

        private int LastSignificantIndex()
        {
            for (var k = tokens.Count - 1; k >= 0; k--)
            {
                if (!string.IsNullOrWhiteSpace(tokens[k].Text) && tokens[k].Kind != Kind.Comment && !_docTokens.Contains(k))
                {
                    return k;
                }
            }
            return -1;
        }

        private string Previous()
        {
            var k = LastSignificantIndex();
            return k < 0 ? "" : tokens[k].Text;
        }

        private Kind LastSignificantKind()
        {
            var k = LastSignificantIndex();
            return k < 0 ? Kind.Text : tokens[k].Kind;
        }

        private string TokenBeforePrevious()
        {
            var seen = false;
            for (var k = tokens.Count - 1; k >= 0; k--)
            {
                if (string.IsNullOrWhiteSpace(tokens[k].Text))
                {
                    continue;
                }
                if (seen)
                {
                    return tokens[k].Text;
                }
                seen = true;
            }
            return "";
        }
    }

    // ---- CSS ------------------------------------------------------------------------------------

    private static readonly HashSet<string> CssValueKeywords =
    [
        "absolute", "auto", "block", "bold", "both", "bottom", "center", "contents", "currentColor", "currentcolor", "dashed", "dotted",
        "ease", "ease-in", "ease-in-out", "ease-out", "fixed", "flex", "forwards", "grid", "hidden", "infinite", "inherit", "initial",
        "inline", "inline-block", "inline-flex", "italic", "left", "linear", "monospace", "none", "normal", "nowrap", "pointer",
        "relative", "revert", "right", "sans-serif", "serif", "solid", "static", "sticky", "system-ui", "top", "transparent",
        "ui-monospace", "ui-sans-serif", "ui-serif", "unset", "visible", "white", "black", "red", "wrap",
    ];

    private static readonly HashSet<string> CssRuleAtRules = ["media", "supports", "layer", "container", "document", "scope", "starting-style", "keyframes"];

    private static readonly HashSet<string> CssDeclarationAtRules = ["font-face", "page", "property", "counter-style"];

    /// <summary>
    /// CSS, as Shiki's CSS grammar scopes it: at-rules, selectors, properties and values. Like that grammar, the body of an
    /// at-rule it doesn't know (Tailwind's @theme, @utility...) reads as selectors.
    /// </summary>
    private sealed class CssScanner(string s, List<(Kind Kind, string Text)> tokens)
    {
        private enum Block { Rules, Declarations, Unknown }

        private readonly Stack<Block> _blocks = new();

        public void Scan(int start, int end)
        {
            var i = start;
            while (i < end)
            {
                i = Step(i, end);
            }
        }

        private void Add(Kind kind, string text)
        {
            if (text.Length > 0)
            {
                tokens.Add((kind, text));
            }
        }

        private int Step(int i, int end)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
            {
                return Whitespace(i, end);
            }
            if (Starts(s, i, "/*"))
            {
                return Comment(i, end);
            }
            if (c == '}')
            {
                Add(Kind.Text, "}");
                if (_blocks.Count > 0)
                {
                    _blocks.Pop();
                }
                return i + 1;
            }
            var block = _blocks.Count == 0 ? Block.Rules : _blocks.Peek();
            if (c == '@')
            {
                return block == Block.Declarations && Starts(s, i, "@apply") ? Apply(i, end) : AtRule(i, end);
            }
            if (block == Block.Rules && IsBareDeclaration(i, end))
            {
                return Declaration(i, end);
            }
            if (block == Block.Rules || IsNestedRule(i, end))
            {
                return Selector(i, end);
            }
            return block == Block.Declarations ? Declaration(i, end) : UnknownStatement(i, end);
        }

        private int Whitespace(int i, int end)
        {
            var k = i;
            while (k < end && char.IsWhiteSpace(s[k]))
            {
                k++;
            }
            Add(Kind.Text, s[i..k]);
            return k;
        }

        private int Comment(int i, int end)
        {
            var close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
            close = close < 0 || close + 2 > end ? end : close + 2;
            Add(Kind.Comment, s[i..close]);
            return close;
        }

        private int AtRule(int i, int end)
        {
            var nameEnd = IdentEnd(i + 1, end);
            var name = s[(i + 1)..nameEnd];
            Add(Kind.Control, s[i..nameEnd]);
            var k = nameEnd;
            var depth = 0;
            var known = CssRuleAtRules.Contains(name) || CssDeclarationAtRules.Contains(name) || name is "import" or "charset" or "namespace";
            while (k < end && !(depth == 0 && s[k] is '{' or ';' or '}'))
            {
                var c = s[k];
                if (!known)
                {
                    Add(Kind.Text, c.ToString());
                    k++;
                    continue;
                }
                if (c is '"' or '\'' && depth == 0)
                {
                    k = QuotedString(k, end);
                    continue;
                }
                if (char.IsDigit(c))
                {
                    k = NumberWithUnit(k, end);
                    continue;
                }
                if (IsIdentStart(c))
                {
                    var e = IdentEnd(k, end);
                    var colon = SkipSpaces(s, e, end);
                    Add(depth > 0 && colon < end && s[colon] == ':' ? Kind.Attribute : Kind.Text, s[k..e]);
                    k = e;
                    continue;
                }
                depth += c switch { '(' => 1, ')' => -1, _ => 0 };
                Add(Kind.Text, c.ToString());
                k++;
            }
            if (k < end && s[k] == '{')
            {
                Add(Kind.Text, "{");
                _blocks.Push(AtRuleBlock(name));
                return k + 1;
            }
            if (k < end && s[k] == ';')
            {
                Add(Kind.Text, ";");
                return k + 1;
            }
            return k;
        }

        private static Block AtRuleBlock(string name)
        {
            if (CssRuleAtRules.Contains(name))
            {
                return Block.Rules;
            }
            return CssDeclarationAtRules.Contains(name) ? Block.Declarations : Block.Unknown;
        }

        /// <summary>Tailwind's <c>@apply</c> in a rule, which the grammar leaves in the default colour.</summary>
        private int Apply(int i, int end)
        {
            var close = s.IndexOfAny([';', '}'], i);
            close = close < 0 || close > end ? end : close;
            if (close < end && s[close] == ';')
            {
                close++;
            }
            Add(Kind.Text, s[i..close]);
            return close;
        }

        /// <summary>A declaration outside any rule, as a snippet shows them: <c>name: value;</c> with no block before the semicolon.</summary>
        private bool IsBareDeclaration(int i, int end)
        {
            if (!(char.IsLetter(s[i]) || Starts(s, i, "--")))
            {
                return false;
            }
            var nameEnd = IdentEnd(i, end);
            var colon = SkipSpaces(s, nameEnd, end);
            if (colon >= end || s[colon] != ':')
            {
                return false;
            }
            var stop = s.IndexOfAny(['{', ';', '}'], colon);
            return stop < 0 || stop >= end || s[stop] != '{';
        }

        private bool IsNestedRule(int i, int end)
        {
            var k = i;
            while (k < end)
            {
                var c = s[k];
                if (c is '"' or '\'')
                {
                    var close = s.IndexOf(c, k + 1);
                    k = close < 0 ? end : close + 1;
                    continue;
                }
                if (c == '{')
                {
                    return true;
                }
                if (c is ';' or '}')
                {
                    return false;
                }
                k++;
            }
            return false;
        }

        private int Selector(int i, int end)
        {
            var k = i;
            while (k < end && s[k] is not ('{' or '}' or ';'))
            {
                var c = s[k];
                if (char.IsWhiteSpace(c))
                {
                    k = Whitespace(k, end);
                }
                else if (Starts(s, k, "/*"))
                {
                    k = Comment(k, end);
                }
                else if (c is '.' or '#' && k + 1 < end && IsIdentStart(s[k + 1]))
                {
                    var e = IdentEnd(k + 1, end);
                    Add(Kind.CssSelector, s[k..e]);
                    k = e;
                }
                else if (c == ':')
                {
                    var e = IdentEnd(k + (Starts(s, k, "::") ? 2 : 1), end);
                    Add(Kind.CssSelector, s[k..e]);
                    k = e;
                }
                else if (c == '[')
                {
                    k = AttributeSelector(k, end);
                }
                else if (c == '*')
                {
                    Add(Kind.Tag, "*");
                    k++;
                }
                else if (IsIdentStart(c))
                {
                    var e = IdentEnd(k, end);
                    Add(Kind.CssSelector, s[k..e]);
                    k = e;
                }
                else if (char.IsDigit(c))
                {
                    k = NumberWithUnit(k, end);
                }
                else
                {
                    Add(Kind.Text, c.ToString());
                    k++;
                }
            }
            if (k < end && s[k] == '{')
            {
                Add(Kind.Text, "{");
                _blocks.Push(Block.Declarations);
                return k + 1;
            }
            if (k == i)
            {
                Add(Kind.Text, s[i].ToString());
                return i + 1;
            }
            return k;
        }

        private int AttributeSelector(int k, int end)
        {
            Add(Kind.Text, "[");
            k++;
            while (k < end && s[k] != ']')
            {
                var c = s[k];
                if (c is '"' or '\'')
                {
                    k = QuotedString(k, end);
                }
                else if (IsIdentStart(c))
                {
                    var e = IdentEnd(k, end);
                    Add(Kind.Attribute, s[k..e]);
                    k = e;
                }
                else
                {
                    Add(Kind.Text, c.ToString());
                    k++;
                }
            }
            if (k < end)
            {
                Add(Kind.Text, "]");
                k++;
            }
            return k;
        }

        private int Declaration(int i, int end)
        {
            var nameEnd = IdentEnd(i, end);
            if (nameEnd == i)
            {
                Add(Kind.Text, s[i].ToString());
                return i + 1;
            }
            var name = s[i..nameEnd];
            Add(Kind.Attribute, name);
            var k = nameEnd;
            while (k < end && s[k] is ' ' or '\t')
            {
                k++;
            }
            Add(Kind.Text, s[nameEnd..k]);
            if (k < end && s[k] == ':')
            {
                Add(Kind.Text, ":");
                k = Value(k + 1, end);
            }
            if (k < end && s[k] == ';')
            {
                Add(Kind.Text, ";");
                k++;
            }
            return k;
        }

        private int Value(int k, int end)
        {
            var depth = 0;
            while (k < end && !(depth == 0 && s[k] is ';' or '}'))
            {
                var c = s[k];
                if (char.IsWhiteSpace(c))
                {
                    k = Whitespace(k, end);
                }
                else if (Starts(s, k, "/*"))
                {
                    k = Comment(k, end);
                }
                else if (c is '"' or '\'')
                {
                    k = QuotedString(k, end);
                }
                else if (c is '(' or ')')
                {
                    depth += c == '(' ? 1 : -1;
                    Add(Kind.Text, c.ToString());
                    k++;
                }
                else if (c == '#' && k + 1 < end && Uri.IsHexDigit(s[k + 1]))
                {
                    var e = k + 1;
                    while (e < end && Uri.IsHexDigit(s[e]))
                    {
                        e++;
                    }
                    Add(Kind.CssValue, s[k..e]);
                    k = e;
                }
                else if (Starts(s, k, "!important"))
                {
                    Add(Kind.Keyword, "!important");
                    k += "!important".Length;
                }
                else if (char.IsDigit(c) || (c is '.' or '-' or '+' && k + 1 < end && (char.IsDigit(s[k + 1]) || (s[k + 1] == '.' && c != '.'))))
                {
                    k = NumberWithUnit(k, end);
                }
                else if (Starts(s, k, "--"))
                {
                    var e = IdentEnd(k, end);
                    Add(Kind.Attribute, s[k..e]);
                    k = e;
                }
                else if (IsIdentStart(c))
                {
                    var e = IdentEnd(k, end);
                    var word = s[k..e];
                    Add(CssValueKind(word, e < end && s[e] == '('), word);
                    k = e;
                }
                else
                {
                    Add(Kind.Text, c.ToString());
                    k++;
                }
            }
            return k;
        }

        /// <summary>A statement in an at-rule's body the grammar doesn't know (<c>--color-x: var(--x);</c> in @theme): selector text.</summary>
        private int UnknownStatement(int i, int end)
        {
            var close = s.IndexOfAny([';', '}'], i);
            close = close < 0 || close > end ? end : close;
            var paren = s.IndexOf(')', i, close - i);
            var split = paren < 0 ? close : paren;
            Add(Kind.Text, s[i..split]);
            var k = split;
            while (k < close)
            {
                if (s[k] == '*')
                {
                    Add(Kind.Tag, "*");
                    k++;
                    continue;
                }
                if (s[k] == '.' && k + 1 < close && char.IsLetterOrDigit(s[k + 1]))
                {
                    var e = k + 1;
                    while (e < close && (char.IsLetterOrDigit(s[e]) || s[e] is '-' or '_'))
                    {
                        e++;
                    }
                    Add(Kind.CssSelector, s[k..e]);
                    k = e;
                    continue;
                }
                Add(Kind.Text, s[k].ToString());
                k++;
            }
            if (close < end && s[close] == ';')
            {
                Add(Kind.Text, ";");
                return close + 1;
            }
            return close;
        }

        private int NumberWithUnit(int k, int end)
        {
            var e = k + 1;
            while (e < end && (char.IsDigit(s[e]) || s[e] == '.'))
            {
                e++;
            }
            Add(Kind.Number, s[k..e]);
            var unit = e;
            while (unit < end && (char.IsLetter(s[unit]) || s[unit] == '%'))
            {
                unit++;
            }
            Add(Kind.Number, s[e..unit]);
            return unit;
        }

        private int QuotedString(int k, int end)
        {
            var close = s.IndexOf(s[k], k + 1);
            close = close < 0 || close >= end ? end - 1 : close;
            Add(Kind.String, s[k..(close + 1)]);
            return close + 1;
        }

        private static Kind CssValueKind(string word, bool call)
        {
            if (call)
            {
                return Kind.Function;
            }
            return CssValueKeywords.Contains(word) ? Kind.CssValue : Kind.Text;
        }

        private static bool IsIdentStart(char c) => char.IsLetter(c) || c is '-' or '_';

        private int IdentEnd(int k, int end)
        {
            while (k < end && (char.IsLetterOrDigit(s[k]) || s[k] is '-' or '_'))
            {
                k++;
            }
            return k;
        }
    }

    // ---- bash, JSON, diff -----------------------------------------------------------------------

    private static void Bash(string s, List<(Kind Kind, string Text)> tokens)
    {
        foreach (var (line, index) in s.Split('\n').Select((l, n) => (l, n)))
        {
            if (index > 0)
            {
                tokens.Add((Kind.Text, "\n"));
            }
            if (line.TrimStart().StartsWith('#'))
            {
                tokens.Add((Kind.Comment, line));
                continue;
            }
            var command = true;
            foreach (var value in BashToken().Matches(line).Select(m => m.Value))
            {
                tokens.Add((BashKind(value, command), value));
                if (!string.IsNullOrWhiteSpace(value))
                {
                    command = value is "&&" or "||" or "|" or ";";
                }
            }
        }
    }

    private static Kind BashKind(string value, bool command)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "&&" or "||" or ";" or "|")
        {
            return Kind.Text;
        }
        if (value.StartsWith('$'))
        {
            return Kind.Variable;
        }
        if (value.StartsWith('#'))
        {
            return Kind.Comment;
        }
        if (command)
        {
            return value == "export" ? Kind.Keyword : Kind.Function;
        }
        if (value.StartsWith('-'))
        {
            return Kind.Keyword;
        }
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? Kind.Number : Kind.String;
    }

    private static void Json(string s, List<(Kind Kind, string Text)> tokens)
    {
        foreach (Match m in JsonToken().Matches(s))
        {
            var value = m.Value;
            Kind kind;
            if (value.StartsWith('"'))
            {
                kind = NextNonSpace(s, m.Index + value.Length) == ':' ? Kind.JsonKey : Kind.String;
            }
            else if (value is "true" or "false" or "null")
            {
                kind = Kind.Constant;
            }
            else
            {
                kind = value.Length > 0 && (char.IsDigit(value[0]) || value[0] == '-') ? Kind.Number : Kind.Text;
            }
            tokens.Add((kind, value));
        }
    }

    private static void Diff(string s, List<(Kind Kind, string Text)> tokens)
    {
        foreach (var (line, index) in s.Split('\n').Select((l, n) => (l, n)))
        {
            if (index > 0)
            {
                tokens.Add((Kind.Text, "\n"));
            }
            var kind = Kind.Text;
            if (line.StartsWith('+'))
            {
                kind = Kind.Inserted;
            }
            else if (line.StartsWith('-'))
            {
                kind = Kind.Deleted;
            }
            tokens.Add((kind, line));
        }
    }

    // ---- Semantic information -------------------------------------------------------------------

    /// <summary>How an attribute's value is highlighted: as markup (a string), as C#, as C# naming a method group, or as a type.</summary>
    private enum ValueKind
    {
        Markup,
        CSharp,
        Method,
        TypeName,
    }

    /// <summary>A name that qualifies a member access: a type, a namespace, or a snippet's enum (by its members).</summary>
    private sealed record Qualifier(Type? Type = null, string? Namespace = null, IReadOnlySet<string>? Members = null)
    {
        /// <summary>The index of the qualifying name's token.</summary>
        public int Token { get; init; } = -1;

        /// <summary>Whether the type has a static member of this name (a snippet's enum's members all are).</summary>
        public bool HasStatic(string member) => Members?.Contains(member) ?? (Type is { } type && Symbols.IsStatic(type, member));
    }

    // Words before a name that don't declare it ("return value;", "x is Foo)").
    private static readonly HashSet<string> NotDeclaring =
    [
        "return", "await", "is", "as", "case", "throw", "new", "yield", "else", "goto", "not", "and", "or", "when", "out", "ref", "in",
        "using", "namespace", "class", "struct", "record", "enum", "interface", "where",
    ];

    // Words before a name and its '(' that make it something other than a method: a constructor's type, a primary constructor.
    private static readonly HashSet<string> NotCalling = ["new", "class", "struct", "record", "interface"];

    /// <summary>
    /// What a snippet declares and imports, for the names reflection can't tell apart: its usings, types, enums, constants,
    /// methods (declared or called), and variables (properties, fields, locals and parameters).
    /// </summary>
    private sealed class SnippetInfo
    {
        private readonly List<(int Start, int End)> _enumBodies = [];

        public SnippetInfo(string code)
        {
            Usings = [.. UsingName().Matches(code).Select(m => m.Groups[1].Value)];
            Types = [.. TypeDeclaration().Matches(code).Select(m => m.Groups[1].Value)];
            Constants = [.. ConstantDeclaration().Matches(code).Select(m => m.Groups[1].Value)];
            var declarations = VariableDeclaration().Matches(code).Where(m => !NotDeclaring.Contains(m.Groups[1].Value)).ToList();
            Variables =
            [
                .. declarations.Select(m => m.Groups[2].Value),
                .. InjectDirective().Matches(code).Select(m => m.Groups[1].Value),
            ];
            ColorColor = [.. declarations.Where(m => m.Groups[1].Value == m.Groups[2].Value).Select(m => m.Groups[2].Value)];
            Methods =
            [
                .. Call().Matches(code)
                    .Where(m => !NotCalling.Contains(m.Groups[1].Value))
                    .Select(m => m.Groups[2].Value)
                    .Where(name => !CSharpKeywords.Contains(name) && !CSharpTypeKeywords.Contains(name) && !Types.Contains(name)),
            ];
            foreach (var groups in EnumDeclaration().Matches(code).Select(match => match.Groups))
            {
                var body = groups[2];
                _enumBodies.Add((body.Index, body.Index + body.Length));
                Enums[groups[1].Value] = LineComment().Replace(body.Value, "").Split(',')
                    .Select(member => EnumMemberName().Match(member).Groups[1].Value)
                    .Where(name => name.Length > 0)
                    .ToHashSet();
            }
        }

        public HashSet<string> Usings { get; }
        public HashSet<string> Types { get; }
        public HashSet<string> Constants { get; }
        public HashSet<string> Variables { get; }

        /// <summary>
        /// The variables of the type they're named after (<c>public Align Align { get; set; }</c>). C#'s "Color Color" rule
        /// binds the name to the type before one of its static members: <c>= Align.Center</c> is the enum's member.
        /// </summary>
        public HashSet<string> ColorColor { get; }

        public HashSet<string> Methods { get; }
        public Dictionary<string, HashSet<string>> Enums { get; } = [];

        public bool InEnumBody(int i) => _enumBodies.Exists(body => i >= body.Start && i < body.End);
    }

    /// <summary>
    /// The types the docs app has loaded (the registry, the primitives, the docs' examples, ASP.NET Core and the BCL), found by
    /// simple name the way the snippets' compilation would bind them. Loaded once, on first use; an assembly that isn't there
    /// (the highlighter outside the app) just has nothing to find.
    /// </summary>
    private static class Symbols
    {
        private const string ComponentInterface = "Microsoft.AspNetCore.Components.IComponent";
        private const string DocsAssembly = "Blazix.Docs.Client";

        // Loaded up front, in case no page has used them yet.
        private static readonly string[] AssemblyNames =
        [
            "Blazix.Registry", "Blazix.Primitives", DocsAssembly, "Microsoft.AspNetCore.Components", "Microsoft.AspNetCore.Components.Web",
            "Microsoft.AspNetCore.Components.Forms", "Microsoft.JSInterop", "System.Collections", "System.Linq", "System.Net.Http",
        ];

        // The namespaces an app's code sees without a using: the web SDK's implicit usings and a Blazor app's _Imports.razor.
        private static readonly HashSet<string> ImplicitNamespaces =
        [
            "System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Net.Http.Json", "System.Threading",
            "System.Threading.Tasks", "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Http",
            "Microsoft.AspNetCore.Routing", "Microsoft.Extensions.Configuration", "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.Hosting", "Microsoft.Extensions.Logging", "Microsoft.AspNetCore.Components",
            "Microsoft.AspNetCore.Components.Forms", "Microsoft.AspNetCore.Components.Routing", "Microsoft.AspNetCore.Components.Web",
            "Microsoft.AspNetCore.Components.Web.Virtualization", "Microsoft.JSInterop",
        ];

        private static readonly Lazy<Catalog> Loaded = new(Load);

        private static readonly ConcurrentDictionary<string, Type[]> ComponentCache = new();

        private sealed class Catalog
        {
            /// <summary>The types by simple name (no generic arity), the app's own assemblies' first.</summary>
            public Dictionary<string, List<Type>> Types { get; } = new(StringComparer.Ordinal);

            /// <summary>Every namespace, and each one's enclosing namespaces.</summary>
            public HashSet<string> Namespaces { get; } = new(StringComparer.Ordinal);
        }

        public static bool IsNamespace(string name) => Loaded.Value.Namespaces.Contains(name);

        /// <summary>The type a simple name binds to: the app's own, or one in an imported namespace (non-generic).</summary>
        public static Type? FindType(string name, SnippetInfo info) =>
            Loaded.Value.Types.TryGetValue(name, out var types)
                ? types.Find(type => !type.IsGenericTypeDefinition && (IsOwn(type) || (type is { IsNested: false, Namespace: { } ns } &&
                                                                                        (ImplicitNamespaces.Contains(ns) || info.Usings.Contains(ns)))))
                : null;

        public static Type? FindQualifiedType(string fullName)
        {
            var name = fullName[(fullName.LastIndexOf('.') + 1)..];
            return Loaded.Value.Types.TryGetValue(name, out var types) ? types.Find(type => type.FullName == fullName) : null;
        }

        public static bool IsComponent(string tag, SnippetInfo info) => Components(tag, info).Length > 0;

        /// <summary>
        /// How a component's attribute's value is highlighted, by the type of the parameter it sets (Razor matches it by exact
        /// name): a string's is markup, a type argument's a type, a delegate's a method group, and anything else's C#.
        /// </summary>
        public static ValueKind? Parameter(string tag, string attribute, SnippetInfo info)
        {
            foreach (var component in Components(tag, info))
            {
                if (component.IsGenericTypeDefinition && Array.Exists(component.GetGenericArguments(), argument => argument.Name == attribute))
                {
                    return ValueKind.TypeName;
                }
                var property = Array.Find(component.GetProperties(BindingFlags.Public | BindingFlags.Instance),
                    property => property.Name == attribute && property.CustomAttributes.Any(a => a.AttributeType.Name == "ParameterAttribute"));
                if (property is not null)
                {
                    return Binding(property.PropertyType);
                }
            }
            return null;
        }

        /// <summary>
        /// What a type's member is, as the semantic tokens colour it, and the type it names if it's a nested one. Null for a
        /// member reflection can't find.
        /// </summary>
        public static (Kind? Kind, Type? Nested) Member(Type type, string name)
        {
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
                return type.GetMember(name, all).FirstOrDefault() switch
                {
                    FieldInfo { IsLiteral: true } => (Kind.ConstantVariable, null),
                    MethodInfo => (Kind.Function, null),
                    Type nested => (Kind.Type, nested),
                    PropertyInfo or FieldInfo or EventInfo => (Kind.Variable, null),
                    _ => (null, null),
                };
            }
            catch (Exception e) when (e is TypeLoadException or IOException or NotSupportedException)
            {
                return (null, null);
            }
        }

        /// <summary>Whether a type has a static member of a name: an enum member, a constant, a static method or property.</summary>
        public static bool IsStatic(Type type, string name)
        {
            try
            {
                return type.GetMember(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).Length > 0;
            }
            catch (Exception e) when (e is TypeLoadException or IOException or NotSupportedException)
            {
                return false;
            }
        }

        private static ValueKind Binding(Type type)
        {
            if (type == typeof(string))
            {
                return ValueKind.Markup;
            }
            var callback = type.Name.StartsWith("EventCallback", StringComparison.Ordinal) ||
                           (typeof(Delegate).IsAssignableFrom(type) && !type.Name.StartsWith("RenderFragment", StringComparison.Ordinal));
            return callback ? ValueKind.Method : ValueKind.CSharp;
        }

        /// <summary>The components a tag can name (its last part, for a dotted tag), the registry's first.</summary>
        private static Type[] Components(string tag, SnippetInfo info) =>
            ComponentCache.GetOrAdd(tag[(tag.LastIndexOf('.') + 1)..], static name =>
                    Loaded.Value.Types.TryGetValue(name, out var types) ? [.. types.Where(IsComponentType)] : [])
                .OrderBy(component => component.Namespace is { } ns && info.Usings.Contains(ns) ? 0 : 1)
                .ToArray();

        private static bool IsComponentType(Type type)
        {
            try
            {
                return !type.IsAbstract && Array.Exists(type.GetInterfaces(), contract => contract.FullName == ComponentInterface);
            }
            catch (Exception e) when (e is TypeLoadException or IOException)
            {
                return false;
            }
        }

        private static bool IsOwn(Type type) => type.Assembly.GetName().Name?.StartsWith("Blazix.", StringComparison.Ordinal) == true;

        private static int Rank(Assembly assembly) => assembly.GetName().Name switch
        {
            "Blazix.Registry" => 0,
            DocsAssembly => 1,
            "Blazix.Primitives" => 2,
            { } name when name.StartsWith("Blazix.", StringComparison.Ordinal) => 3,
            _ => 4,
        };

        private static Catalog Load()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().ToList();
            foreach (var name in AssemblyNames)
            {
                try
                {
                    assemblies.Add(Assembly.Load(name));
                }
                catch (Exception e) when (e is IOException or BadImageFormatException)
                {
                    // Not part of this app: its types just aren't found.
                }
            }
            var catalog = new Catalog();
            foreach (var type in assemblies.Distinct().Where(assembly => !assembly.IsDynamic).OrderBy(Rank).SelectMany(TypesOf))
            {
                var name = type.Name;
                if (name.Contains('<') || name.StartsWith("__", StringComparison.Ordinal))
                {
                    continue;
                }
                var tick = name.IndexOf('`');
                var simpleName = tick < 0 ? name : name[..tick];
                if (!catalog.Types.TryGetValue(simpleName, out var types))
                {
                    catalog.Types[simpleName] = types = [];
                }
                types.Add(type);
                for (var ns = type.Namespace; !string.IsNullOrEmpty(ns); ns = ns.LastIndexOf('.') is var dot and > 0 ? ns[..dot] : null)
                {
                    catalog.Namespaces.Add(ns);
                }
            }
            return catalog;
        }

        private static IEnumerable<Type> TypesOf(Assembly assembly)
        {
            try
            {
                // The docs' own types include its examples' private ones (a demo's nested Payment record).
                return assembly.GetName().Name == DocsAssembly ? assembly.GetTypes() : assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.OfType<Type>();
            }
            catch (Exception e) when (e is NotSupportedException or IOException or TypeLoadException)
            {
                return [];
            }
        }
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static bool Starts(string s, int i, string value) =>
        i >= 0 && i + value.Length <= s.Length && string.CompareOrdinal(s, i, value, 0, value.Length) == 0;

    private static int SkipSpaces(string s, int i, int end)
    {
        while (i < end && s[i] is ' ' or '\t')
        {
            i++;
        }
        return i;
    }

    private static char NextNonSpace(string s, int i)
    {
        while (i < s.Length && s[i] == ' ')
        {
            i++;
        }
        return i < s.Length ? s[i] : '\0';
    }

    private static int LineEnd(string s, int i, int end)
    {
        var lineEnd = s.IndexOf('\n', i);
        return lineEnd < 0 || lineEnd > end ? end : lineEnd;
    }

    /// <summary>The end of an HTML entity (<c>&amp;amp;</c>, <c>&amp;#39;</c>) at <paramref name="i"/>, or <paramref name="i"/>.</summary>
    private static int EntityEnd(string s, int i, int end)
    {
        var m = Entity().Match(s, i, Math.Min(12, end - i));
        return m.Success && m.Index == i ? i + m.Length : i;
    }

    /// <summary>The index just past the bracket that closes the one at <paramref name="open"/>, skipping strings.</summary>
    private static int Matching(string s, int open, char openChar, char closeChar)
    {
        var depth = 0;
        var i = open;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '"')
            {
                i = SkipString(s, i);
                continue;
            }
            if (c == openChar)
            {
                depth++;
            }
            else if (c == closeChar && --depth == 0)
            {
                return i + 1;
            }
            i++;
        }
        return s.Length;
    }

    /// <summary>The index just past the C# string starting at <paramref name="i"/> (plain or raw).</summary>
    private static int SkipString(string s, int i)
    {
        if (Starts(s, i, "\"\"\""))
        {
            var raw = s.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
            return raw < 0 ? s.Length : raw + 3;
        }
        i++;
        while (i < s.Length && s[i] != '"' && s[i] != '\n')
        {
            i += s[i] == '\\' ? 2 : 1;
        }
        return Math.Min(s.Length, i + 1);
    }

    [GeneratedRegex(@"\s+|""[^""]*""|'[^']*'|#.*$|&&|\|\||[|;]|[^\s|;]+")]
    private static partial Regex BashToken();

    [GeneratedRegex(@"""(?:[^""\\]|\\.)*""|-?\d+(?:\.\d+)?|true|false|null|\s+|.")]
    private static partial Regex JsonToken();

    [GeneratedRegex(@"^using\s+(?:static\s+)?[\w.]+\s*(?:=\s*[\w.<>, ]+)?;\s*$")]
    private static partial Regex UsingDirectiveLine();

    [GeneratedRegex(@"&(?:[A-Za-z]+|#\d+|#x[0-9A-Fa-f]+);")]
    private static partial Regex Entity();

    [GeneratedRegex(@"^[ \t]*@?using[ \t]+(?:static[ \t]+)?(?:\w+[ \t]*=[ \t]*)?([\w.]+)[ \t]*;?[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex UsingName();

    [GeneratedRegex(@"\b(?:class|struct|interface|enum|record)[ \t]+(?:(?:class|struct)[ \t]+)?([A-Za-z_]\w*)")]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(@"\benum[ \t]+([A-Za-z_]\w*)[^{;]*\{([^}]*)\}")]
    private static partial Regex EnumDeclaration();

    [GeneratedRegex(@"^\s*(?:\[[^\]]*\]\s*)*([A-Za-z_]\w*)")]
    private static partial Regex EnumMemberName();

    [GeneratedRegex("//.*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"\bconst[ \t]+[\w.<>?,\[\] \t]+?[ \t]+([A-Za-z_]\w*)[ \t]*=")]
    private static partial Regex ConstantDeclaration();

    // A name after a type (its word captured, a nullable's without the '?') or another word, that a declaration's ';', ',',
    // ')', '=', '{ get' or 'in' follows. Not a markup attribute, whose '=' a quote follows: <PopoverContent Align="Align.Start">.
    [GeneratedRegex(@"(?:([A-Za-z_]\w*)\??|[>\]?])[ \t]+@?([A-Za-z_]\w*)[ \t]*(?:[;,)]|=(?![=""'])|\{[ \t]*(?:get|set|init)\b|in\b)")]
    private static partial Regex VariableDeclaration();

    [GeneratedRegex(@"@inject[ \t]+\S+[ \t]+([A-Za-z_]\w*)")]
    private static partial Regex InjectDirective();

    // A name and its '(' (with type arguments between): a method's declaration or call, and the word before it.
    [GeneratedRegex(@"(?:\b(\w+)[ \t]+)?\b([A-Za-z_]\w*)(?:<[\w \t,.<>?\[\]]*>)?\(")]
    private static partial Regex Call();
}
