using LilAgents.Core;
using LilAgents.Themes;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace LilAgents.Views;

/// <summary>
/// Chat display with hand-rolled markdown rendering via RichTextBlock.
/// Streaming appends Run/Paragraph elements incrementally.
/// </summary>
public sealed partial class ChatView : UserControl
{
    private string _currentAssistantText = "";
    private bool _isStreaming;
    private RichTextBlock? _streamingBlock;
    private readonly MarkdownRenderer _markdown = new();

    public event Action<string>? MessageSubmitted;

    public ChatView()
    {
        InitializeComponent();
    }

    // ── Theme ────────────────────────────────────────────────────────

    private PopoverTheme _theme = PopoverTheme.Peach;

    public PopoverTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            ApplyTheme();
        }
    }

    private void ApplyTheme()
    {
        var t = _theme;
        // Force fully opaque — macOS alpha < 255 is for NSColor compositing,
        // not window transparency. On Windows it causes desktop bleed-through.
        var bg = t.PopoverBg;
        RootGrid.Background = new SolidColorBrush(
            Windows.UI.Color.FromArgb(255, bg.R, bg.G, bg.B));
        InputSeparator.Background = new SolidColorBrush(t.SeparatorColor);
        InputBox.FontFamily = new FontFamily(t.FontFamily);
        InputBox.FontSize = t.FontSize;
        InputBox.Foreground = new SolidColorBrush(t.TextPrimary);
        InputBox.PlaceholderForeground = new SolidColorBrush(t.TextDim);
        InputBox.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        InputBox.BorderThickness = new Thickness(0);
    }

    // ── Input handling ───────────────────────────────────────────────

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;

        var text = InputBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        InputBox.Text = "";
        AppendUser(text);
        _isStreaming = true;
        _currentAssistantText = "";
        _streamingBlock = null;
        _markdown.Reset();
        MessageSubmitted?.Invoke(text);
        e.Handled = true;
    }

    public void FocusInput() => InputBox.Focus(FocusState.Programmatic);

    // ── Public append methods ────────────────────────────────────────

    public void AppendUser(string text)
    {
        var t = _theme;
        var rtb = new RichTextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(t.TextPrimary),
            Margin = new Thickness(0, 8, 0, 0),
        };

        var para = new Paragraph { LineStackingStrategy = LineStackingStrategy.MaxHeight };
        para.Inlines.Add(new Run
        {
            Text = "> ",
            FontFamily = new FontFamily(t.FontBoldFamily),
            FontSize = t.FontSize,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(t.AccentColor),
        });
        para.Inlines.Add(new Run
        {
            Text = text,
            FontFamily = new FontFamily(t.FontBoldFamily),
            FontSize = t.FontSize,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(t.TextPrimary),
        });

        rtb.Blocks.Add(para);
        MessagesPanel.Children.Add(rtb);
        ScrollToBottom();
    }

    public void AppendStreamingText(string text)
    {
        var cleaned = text;
        if (_currentAssistantText.Length == 0)
            cleaned = cleaned.TrimStart('\n');
        _currentAssistantText += cleaned;
        if (cleaned.Length == 0) return;

        if (_streamingBlock is null)
        {
            _streamingBlock = new RichTextBlock
            {
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(_theme.TextPrimary),
                Margin = new Thickness(0, 2, 0, 0),
            };
            MessagesPanel.Children.Add(_streamingBlock);
        }

        _markdown.AppendMarkdown(_streamingBlock, cleaned, _theme, MessagesPanel, ref _streamingBlock);
        ScrollToBottom();
    }

    public void EndStreaming()
    {
        if (_isStreaming)
        {
            _isStreaming = false;
            _streamingBlock = null;
            _markdown.Reset();
        }
    }

    public void AppendError(string text)
    {
        var t = _theme;
        var rtb = new RichTextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(t.TextPrimary),
            Margin = new Thickness(0, 2, 0, 0),
        };
        var para = new Paragraph();
        para.Inlines.Add(new Run
        {
            Text = text,
            FontFamily = new FontFamily(t.FontFamily),
            FontSize = t.FontSize,
            Foreground = new SolidColorBrush(t.ErrorColor),
        });
        rtb.Blocks.Add(para);
        MessagesPanel.Children.Add(rtb);
        ScrollToBottom();
    }

    public void AppendToolUse(string toolName, string summary)
    {
        EndStreaming();
        var t = _theme;
        var rtb = new RichTextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(t.TextPrimary),
            Margin = new Thickness(0, 2, 0, 0),
        };
        var para = new Paragraph();
        para.Inlines.Add(new Run
        {
            Text = $"  {toolName.ToUpperInvariant()} ",
            FontFamily = new FontFamily(t.FontBoldFamily),
            FontSize = t.FontSize,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(t.AccentColor),
        });
        para.Inlines.Add(new Run
        {
            Text = summary,
            FontFamily = new FontFamily(t.FontFamily),
            FontSize = t.FontSize,
            Foreground = new SolidColorBrush(t.TextDim),
        });
        rtb.Blocks.Add(para);
        MessagesPanel.Children.Add(rtb);
        ScrollToBottom();
    }

    public void AppendToolResult(string summary, bool isError)
    {
        var t = _theme;
        var color = isError ? t.ErrorColor : t.SuccessColor;
        var prefix = isError ? "  FAIL " : "  DONE ";

        var rtb = new RichTextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(t.TextPrimary),
        };
        var para = new Paragraph();
        para.Inlines.Add(new Run
        {
            Text = prefix,
            FontFamily = new FontFamily(t.FontBoldFamily),
            FontSize = t.FontSize,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(color),
        });
        para.Inlines.Add(new Run
        {
            Text = summary,
            FontFamily = new FontFamily(t.FontFamily),
            FontSize = t.FontSize,
            Foreground = new SolidColorBrush(t.TextDim),
        });
        rtb.Blocks.Add(para);
        MessagesPanel.Children.Add(rtb);
        ScrollToBottom();
    }

    // ── History replay ───────────────────────────────────────────────

    public void ReplayHistory(IReadOnlyList<ChatMessage> messages)
    {
        MessagesPanel.Children.Clear();
        _streamingBlock = null;
        _isStreaming = false;
        _currentAssistantText = "";
        _markdown.Reset();

        foreach (var msg in messages)
        {
            switch (msg.Role)
            {
                case ChatMessageRole.User:
                    AppendUser(msg.Text);
                    break;
                case ChatMessageRole.Assistant:
                    AppendCompleteMarkdown(msg.Text);
                    break;
                case ChatMessageRole.Error:
                    AppendError(msg.Text);
                    break;
                case ChatMessageRole.ToolUse:
                    AppendToolLine(msg.Text, _theme.AccentColor);
                    break;
                case ChatMessageRole.ToolResult:
                    var isErr = msg.Text.StartsWith("ERROR:");
                    AppendToolLine(msg.Text, isErr ? _theme.ErrorColor : _theme.SuccessColor);
                    break;
            }
        }
        ScrollToBottom();
    }

    private void AppendCompleteMarkdown(string text)
    {
        var rtb = new RichTextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(_theme.TextPrimary),
            Margin = new Thickness(0, 2, 0, 0),
        };
        RichTextBlock? blockRef = rtb;
        var renderer = new MarkdownRenderer();
        renderer.AppendMarkdown(rtb, text, _theme, MessagesPanel, ref blockRef);
        MessagesPanel.Children.Add(rtb);
    }

    private void AppendToolLine(string text, Windows.UI.Color color)
    {
        var t = _theme;
        var rtb = new RichTextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(t.TextPrimary),
            Margin = new Thickness(0, 2, 0, 0),
        };
        var para = new Paragraph();
        para.Inlines.Add(new Run
        {
            Text = $"  {text}",
            FontFamily = new FontFamily(t.FontFamily),
            FontSize = t.FontSize,
            Foreground = new SolidColorBrush(color),
        });
        rtb.Blocks.Add(para);
        MessagesPanel.Children.Add(rtb);
    }

    public void Clear()
    {
        MessagesPanel.Children.Clear();
        _streamingBlock = null;
        _isStreaming = false;
        _currentAssistantText = "";
        _markdown.Reset();
    }

    // ── Unused stubs (kept for interface compat) ─────────────────────

    public void CompletePendingSwap() { }
    public void RefreshMarkdown() { }

    // ── Scrolling ────────────────────────────────────────────────────

    public void ScrollToBottom()
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            ChatScroller.ChangeView(null, ChatScroller.ScrollableHeight, null, disableAnimation: true);
        });
    }
}
