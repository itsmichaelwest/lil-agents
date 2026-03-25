using System.Text.RegularExpressions;
using LilAgents.Themes;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace LilAgents.Views;

/// <summary>
/// Renders markdown into WinUI RichTextBlock content.
/// Supports: headers (h1-h3), code blocks, inline code, bold, italic,
/// bold-italic, strikethrough, markdown links, bare URLs, bullet lists,
/// numbered lists, blockquotes, horizontal rules, and tables.
/// </summary>
internal sealed class MarkdownRenderer
{
    private bool _inCodeBlock;
    private string _codeBlockLang = "";
    private readonly List<string> _codeLines = [];

    // Table accumulation
    private bool _inTable;
    private readonly List<string> _tableRows = [];

    public void AppendMarkdown(
        RichTextBlock rtb,
        string text,
        PopoverTheme theme,
        StackPanel messagesPanel,
        ref RichTextBlock? streamingBlockRef)
    {
        var lines = text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isLastLine = i == lines.Length - 1;

            // Code fence toggle
            if (line.StartsWith("```"))
            {
                FlushTable(rtb, theme, messagesPanel, ref streamingBlockRef);
                if (streamingBlockRef is not null) rtb = streamingBlockRef;

                if (_inCodeBlock)
                {
                    FlushCodeBlock(rtb, theme, messagesPanel, ref streamingBlockRef);
                    if (streamingBlockRef is not null) rtb = streamingBlockRef;
                }
                else
                {
                    _inCodeBlock = true;
                    _codeBlockLang = line.Length > 3 ? line[3..].Trim() : "";
                    _codeLines.Clear();
                }
                continue;
            }

            if (_inCodeBlock)
            {
                _codeLines.Add(line);
                continue;
            }

            // Table row detection (line contains | and isn't a separator-only line)
            if (line.Contains('|') && line.TrimStart().StartsWith('|'))
            {
                _tableRows.Add(line);
                continue;
            }
            else if (_inTable || _tableRows.Count > 0)
            {
                FlushTable(rtb, theme, messagesPanel, ref streamingBlockRef);
                if (streamingBlockRef is not null) rtb = streamingBlockRef;
            }

            // Horizontal rule
            if (line.Trim() is "---" or "***" or "___")
            {
                var rule = new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(theme.SeparatorColor),
                    Margin = new Thickness(0, 4, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                messagesPanel.Children.Add(rule);

                // Fresh block after rule
                rtb = CreateFreshBlock(theme, messagesPanel, ref streamingBlockRef);
                continue;
            }

            var para = GetOrCreateLastParagraph(rtb);

            // Blockquote
            if (line.StartsWith("> "))
            {
                para.Inlines.Add(new Run
                {
                    Text = "\u2502 ",
                    FontFamily = new FontFamily(theme.FontFamily),
                    FontSize = theme.FontSize,
                    Foreground = new SolidColorBrush(theme.AccentColor),
                });
                AddInlineMarkdown(para, line[2..], theme);
            }
            // Headers
            else if (line.StartsWith("#### "))
                AddHeaderRun(para, line[5..], theme, 0);
            else if (line.StartsWith("### "))
                AddHeaderRun(para, line[4..], theme, 0);
            else if (line.StartsWith("## "))
                AddHeaderRun(para, line[3..], theme, 1);
            else if (line.StartsWith("# "))
                AddHeaderRun(para, line[2..], theme, 2);
            // Bullet lists
            else if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                para.Inlines.Add(new Run
                {
                    Text = "  \u2022 ",
                    FontFamily = new FontFamily(theme.FontFamily),
                    FontSize = theme.FontSize,
                    Foreground = new SolidColorBrush(theme.AccentColor),
                });
                AddInlineMarkdown(para, line[2..], theme);
            }
            // Nested bullet (2+ spaces + - )
            else if (Regex.IsMatch(line, @"^  +[-*] "))
            {
                var content = Regex.Replace(line, @"^  +([-*]) ", "");
                para.Inlines.Add(new Run
                {
                    Text = "    \u25E6 ",
                    FontFamily = new FontFamily(theme.FontFamily),
                    FontSize = theme.FontSize,
                    Foreground = new SolidColorBrush(theme.TextDim),
                });
                AddInlineMarkdown(para, content, theme);
            }
            // Numbered list
            else if (Regex.IsMatch(line, @"^\d+\. "))
            {
                var match = Regex.Match(line, @"^(\d+)\. (.*)");
                if (match.Success)
                {
                    para.Inlines.Add(new Run
                    {
                        Text = $"  {match.Groups[1].Value}. ",
                        FontFamily = new FontFamily(theme.FontFamily),
                        FontSize = theme.FontSize,
                        Foreground = new SolidColorBrush(theme.AccentColor),
                    });
                    AddInlineMarkdown(para, match.Groups[2].Value, theme);
                }
            }
            // Plain text
            else
            {
                AddInlineMarkdown(para, line, theme);
            }

            if (!isLastLine)
            {
                para.Inlines.Add(new LineBreak());
                rtb.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
            }
        }
    }

    public void Reset()
    {
        _inCodeBlock = false;
        _codeBlockLang = "";
        _codeLines.Clear();
        _inTable = false;
        _tableRows.Clear();
    }

    // ── Code block ────────────────────────────────────────────────────

    private void FlushCodeBlock(
        RichTextBlock rtb, PopoverTheme t,
        StackPanel messagesPanel, ref RichTextBlock? streamingBlockRef)
    {
        _inCodeBlock = false;
        if (_codeLines.Count == 0) return;

        var codeText = string.Join("\n", _codeLines);
        _codeLines.Clear();

        var codeBorder = new Border
        {
            Background = new SolidColorBrush(t.InputBg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 2, 0, 2),
        };

        var codeRtb = new RichTextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(t.TextPrimary),
        };
        var codePara = new Paragraph();
        codePara.Inlines.Add(new Run
        {
            Text = codeText,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = t.FontSize - 1,
            Foreground = new SolidColorBrush(t.TextPrimary),
        });
        codeRtb.Blocks.Add(codePara);
        codeBorder.Child = codeRtb;

        messagesPanel.Children.Add(codeBorder);
        CreateFreshBlock(t, messagesPanel, ref streamingBlockRef);
    }

    // ── Table ─────────────────────────────────────────────────────────

    private void FlushTable(
        RichTextBlock rtb, PopoverTheme t,
        StackPanel messagesPanel, ref RichTextBlock? streamingBlockRef)
    {
        if (_tableRows.Count == 0) return;

        // Parse rows into cells
        var rows = new List<string[]>();
        int? separatorRow = null;
        for (int r = 0; r < _tableRows.Count; r++)
        {
            var cells = ParseTableRow(_tableRows[r]);
            // Detect separator row (---|---|---)
            if (cells.All(c => Regex.IsMatch(c.Trim(), @"^:?-+:?$")))
            {
                separatorRow = r;
                continue;
            }
            rows.Add(cells);
        }
        _tableRows.Clear();
        _inTable = false;

        if (rows.Count == 0) return;

        int cols = rows.Max(r => r.Length);

        // Build a Grid for the table
        var grid = new Grid
        {
            Margin = new Thickness(0, 4, 0, 4),
            BorderBrush = new SolidColorBrush(t.SeparatorColor),
            BorderThickness = new Thickness(1),
        };

        for (int c = 0; c < cols; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < rows.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int r = 0; r < rows.Count; r++)
        {
            bool isHeader = separatorRow.HasValue && r == 0;
            for (int c = 0; c < cols; c++)
            {
                var cellText = c < rows[r].Length ? rows[r][c].Trim() : "";
                var cellBorder = new Border
                {
                    BorderBrush = new SolidColorBrush(t.SeparatorColor),
                    BorderThickness = new Thickness(0, 0, c < cols - 1 ? 1 : 0, r < rows.Count - 1 ? 1 : 0),
                    Padding = new Thickness(6, 3, 6, 3),
                    Background = isHeader
                        ? new SolidColorBrush(t.InputBg)
                        : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                };

                var tb = new TextBlock
                {
                    Text = cellText,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily(isHeader ? t.FontBoldFamily : t.FontFamily),
                    FontSize = t.FontSize - 0.5,
                    FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = new SolidColorBrush(t.TextPrimary),
                };
                cellBorder.Child = tb;

                Grid.SetRow(cellBorder, r);
                Grid.SetColumn(cellBorder, c);
                grid.Children.Add(cellBorder);
            }
        }

        messagesPanel.Children.Add(grid);
        CreateFreshBlock(t, messagesPanel, ref streamingBlockRef);
    }

    private static string[] ParseTableRow(string row)
    {
        var trimmed = row.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];
        return trimmed.Split('|');
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static RichTextBlock CreateFreshBlock(
        PopoverTheme t, StackPanel messagesPanel, ref RichTextBlock? streamingBlockRef)
    {
        var newBlock = new RichTextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(t.TextPrimary),
        };
        messagesPanel.Children.Add(newBlock);
        streamingBlockRef = newBlock;
        return newBlock;
    }

    private static Paragraph GetOrCreateLastParagraph(RichTextBlock rtb)
    {
        if (rtb.Blocks.Count > 0 && rtb.Blocks[^1] is Paragraph last)
            return last;

        var p = new Paragraph();
        rtb.Blocks.Add(p);
        return p;
    }

    private static void AddHeaderRun(Paragraph para, string text, PopoverTheme t, int sizeOffset)
    {
        para.Inlines.Add(new Run
        {
            Text = text,
            FontFamily = new FontFamily(t.FontBoldFamily),
            FontSize = t.FontSize + sizeOffset,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(t.AccentColor),
        });
    }

    // ── Inline markdown ───────────────────────────────────────────────

    // Order matters: bold-italic before bold before italic
    private static readonly Regex InlinePattern = new(
        @"(`[^`]+`)" +                     // inline code
        @"|(\*\*\*[^*]+\*\*\*)" +          // bold italic ***text***
        @"|(\*\*[^*]+\*\*)" +              // bold **text**
        @"|(\*[^*]+\*)" +                  // italic *text*
        @"|(~~[^~]+~~)" +                  // strikethrough ~~text~~
        @"|(\[[^\]]+\]\([^)]+\))" +        // link [text](url)
        @"|(https?://[^\s)>\]]+)",          // bare URL
        RegexOptions.Compiled);

    private static void AddInlineMarkdown(Paragraph para, string text, PopoverTheme t)
    {
        var lastIndex = 0;

        foreach (Match match in InlinePattern.Matches(text))
        {
            if (match.Index > lastIndex)
                para.Inlines.Add(MakePlainRun(text[lastIndex..match.Index], t));

            if (match.Groups[1].Success)
            {
                // Inline code
                var code = match.Value[1..^1];
                para.Inlines.Add(new Run
                {
                    Text = code,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                    FontSize = t.FontSize - 0.5,
                    Foreground = new SolidColorBrush(t.AccentColor),
                });
            }
            else if (match.Groups[2].Success)
            {
                // Bold italic
                var boldItalic = match.Value[3..^3];
                para.Inlines.Add(new Run
                {
                    Text = boldItalic,
                    FontFamily = new FontFamily(t.FontBoldFamily),
                    FontSize = t.FontSize,
                    FontWeight = FontWeights.Bold,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(t.TextPrimary),
                });
            }
            else if (match.Groups[3].Success)
            {
                // Bold
                var bold = match.Value[2..^2];
                para.Inlines.Add(new Run
                {
                    Text = bold,
                    FontFamily = new FontFamily(t.FontBoldFamily),
                    FontSize = t.FontSize,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(t.TextPrimary),
                });
            }
            else if (match.Groups[4].Success)
            {
                // Italic
                var italic = match.Value[1..^1];
                para.Inlines.Add(new Run
                {
                    Text = italic,
                    FontFamily = new FontFamily(t.FontFamily),
                    FontSize = t.FontSize,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(t.TextPrimary),
                });
            }
            else if (match.Groups[5].Success)
            {
                // Strikethrough
                var struck = match.Value[2..^2];
                para.Inlines.Add(new Run
                {
                    Text = struck,
                    FontFamily = new FontFamily(t.FontFamily),
                    FontSize = t.FontSize,
                    Foreground = new SolidColorBrush(t.TextDim),
                    TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough,
                });
            }
            else if (match.Groups[6].Success)
            {
                // Link [text](url)
                var linkMatch = Regex.Match(match.Value, @"\[([^\]]+)\]\(([^)]+)\)");
                if (linkMatch.Success)
                {
                    var linkText = linkMatch.Groups[1].Value;
                    var urlStr = linkMatch.Groups[2].Value;
                    AddHyperlink(para, linkText, urlStr, t);
                }
            }
            else if (match.Groups[7].Success)
            {
                // Bare URL
                AddHyperlink(para, match.Value, match.Value, t);
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
            para.Inlines.Add(MakePlainRun(text[lastIndex..], t));
    }

    private static void AddHyperlink(Paragraph para, string text, string url, PopoverTheme t)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var hyperlink = new Hyperlink
            {
                NavigateUri = uri,
                Foreground = new SolidColorBrush(t.AccentColor),
                UnderlineStyle = UnderlineStyle.Single,
            };
            hyperlink.Inlines.Add(new Run
            {
                Text = text,
                FontFamily = new FontFamily(t.FontFamily),
                FontSize = t.FontSize,
            });
            para.Inlines.Add(hyperlink);
        }
        else
        {
            para.Inlines.Add(MakePlainRun(text, t));
        }
    }

    private static Run MakePlainRun(string text, PopoverTheme t) => new()
    {
        Text = text,
        FontFamily = new FontFamily(t.FontFamily),
        FontSize = t.FontSize,
        Foreground = new SolidColorBrush(t.TextPrimary),
    };
}
