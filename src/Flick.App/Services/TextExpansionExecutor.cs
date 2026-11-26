using System.Runtime.InteropServices;
using Flick.Core.Models;
using Flick.Core.TextExpansion;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Flick.App.Services;

/// <summary>
/// Executes matched <see cref="TextExpansionRule"/> instances: deletes typed abbreviation,
/// collects interactive {input}/{choice} prompts if present, and injects text, keys, delays,
/// and {cursor} movement into the target window.
/// </summary>
public sealed class TextExpansionExecutor
{
    public void Execute(
        TextExpansionRule rule,
        TextExpansionMatchResult match,
        ForegroundAppInfo targetApp,
        IReadOnlyList<TextExpansionRule> allRules,
        DispatcherQueue? uiDispatcher)
    {
        var prompts = TextExpansionTemplateEngine.ExtractPromptFields(rule.Template);
        if (prompts.Count > 0 && uiDispatcher is not null)
        {
            uiDispatcher.TryEnqueue(async () =>
            {
                var answers = await PromptUserAsync(rule, prompts).ConfigureAwait(true);
                if (answers is null)
                {
                    // User cancelled: if delimiter was swallowed, leave abbreviation intact or re-emit delimiter.
                    return;
                }

                if (targetApp.WindowHandle != 0)
                {
                    NativeMethods.SetForegroundWindow(targetApp.WindowHandle);
                    await Task.Delay(50).ConfigureAwait(false);
                }

                RunOnSta(() => ExecuteCore(rule, match, targetApp, allRules, answers));
            });
            return;
        }

        RunOnSta(() => ExecuteCore(rule, match, targetApp, allRules, promptAnswers: null));
    }

    private static void RunOnSta(Action action)
    {
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch
            {
                // Keep background expansion resilient against transient target/clipboard errors.
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    private static void ExecuteCore(
        TextExpansionRule rule,
        TextExpansionMatchResult match,
        ForegroundAppInfo targetApp,
        IReadOnlyList<TextExpansionRule> allRules,
        IReadOnlyDictionary<string, string>? promptAnswers)
    {
        string clipboardText = ReadClipboardTextSafe();
        var context = new ExpansionContext(
            ClipboardText: clipboardText,
            WindowTitle: targetApp.WindowTitle,
            ProcessName: targetApp.ProcessName,
            AllRules: allRules,
            PromptAnswers: promptAnswers);

        var steps = TextExpansionTemplateEngine.Evaluate(
            rule,
            match.TypedAbbreviation,
            match.DelimiterChar,
            context);

        if (match.BackspaceCount > 0)
        {
            SendKeyRepeated(0x08, match.BackspaceCount); // VK_BACK
            Thread.Sleep(15);
        }

        foreach (var step in steps)
        {
            switch (step.Kind)
            {
                case ExpansionStepKind.Text:
                    EmitText(step.Value, rule.InjectionMode, clipboardText);
                    break;

                case ExpansionStepKind.Key:
                    ActionExecutor.SendHotkey(step.Value);
                    break;

                case ExpansionStepKind.Delay:
                    if (step.Count > 0)
                    {
                        Thread.Sleep(step.Count);
                    }
                    break;

                case ExpansionStepKind.MoveCursorLeft:
                    if (step.Count > 0)
                    {
                        SendKeyRepeated(0x25, step.Count, extended: true); // VK_LEFT
                    }
                    break;
            }
        }
    }

    private static void EmitText(string text, ExpansionInjectionMode mode, string previousClipboard)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        bool useClipboard = mode == ExpansionInjectionMode.ClipboardPaste ||
                            (mode == ExpansionInjectionMode.Auto && (text.Length > 40 || text.Contains('\n')));

        if (useClipboard && TrySetClipboardText(text))
        {
            Thread.Sleep(25);
            SendCtrlV();
            Thread.Sleep(110);
            if (!string.IsNullOrEmpty(previousClipboard))
            {
                TrySetClipboardText(previousClipboard);
            }
            return;
        }

        SendUnicodeString(text);
    }

    private static void SendUnicodeString(string text)
    {
        var batch = new List<NativeMethods.INPUT>(text.Length * 2);

        void FlushBatch()
        {
            if (batch.Count == 0) return;
            _ = NativeMethods.SendInput((uint)batch.Count, batch.ToArray(), Marshal.SizeOf<NativeMethods.INPUT>());
            batch.Clear();
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n' && i > 0 && text[i - 1] == '\r')
            {
                continue;
            }

            if (c is '\r' or '\n')
            {
                FlushBatch();
                batch.Add(CreateVkInput(0x0D, keyUp: false));
                batch.Add(CreateVkInput(0x0D, keyUp: true));
                FlushBatch();
                Thread.Sleep(15);
            }
            else if (c == '\t')
            {
                FlushBatch();
                batch.Add(CreateVkInput(0x09, keyUp: false));
                batch.Add(CreateVkInput(0x09, keyUp: true));
                FlushBatch();
                Thread.Sleep(10);
            }
            else
            {
                batch.Add(new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_KEYBOARD,
                    u = new NativeMethods.INPUTUNION
                    {
                        ki = new NativeMethods.KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = c,
                            dwFlags = NativeMethods.KEYEVENTF_UNICODE,
                            dwExtraInfo = KeyboardChordHook.FlickInjectedTag
                        }
                    }
                });
                batch.Add(new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_KEYBOARD,
                    u = new NativeMethods.INPUTUNION
                    {
                        ki = new NativeMethods.KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = c,
                            dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP,
                            dwExtraInfo = KeyboardChordHook.FlickInjectedTag
                        }
                    }
                });
            }
        }

        FlushBatch();
    }

    private static void SendKeyRepeated(ushort vk, int count, bool extended = false)
    {
        if (count <= 0)
        {
            return;
        }

        var inputs = new NativeMethods.INPUT[count * 2];
        for (int i = 0; i < count; i++)
        {
            inputs[i * 2] = CreateVkInput(vk, keyUp: false, extended);
            inputs[i * 2 + 1] = CreateVkInput(vk, keyUp: true, extended);
        }

        _ = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            CreateVkInput(0x11, keyUp: false), // VK_CONTROL
            CreateVkInput('V', keyUp: false),
            CreateVkInput('V', keyUp: true),
            CreateVkInput(0x11, keyUp: true)
        };
        _ = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT CreateVkInput(ushort vk, bool keyUp, bool extended = false)
    {
        uint flags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0;
        if (extended)
        {
            flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
        }

        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = flags,
                    dwExtraInfo = KeyboardChordHook.FlickInjectedTag
                }
            }
        };
    }

    private static string ReadClipboardTextSafe()
    {
        if (!NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT))
        {
            return string.Empty;
        }

        if (!TryOpenClipboardWithRetry())
        {
            return string.Empty;
        }

        try
        {
            nint hData = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
            if (hData == 0) return string.Empty;

            nint ptr = NativeMethods.GlobalLock(hData);
            if (ptr == 0) return string.Empty;

            try
            {
                return Marshal.PtrToStringUni(ptr) ?? string.Empty;
            }
            finally
            {
                NativeMethods.GlobalUnlock(hData);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool TrySetClipboardText(string text)
    {
        if (!TryOpenClipboardWithRetry())
        {
            return false;
        }

        try
        {
            if (!NativeMethods.EmptyClipboard())
            {
                return false;
            }

            // Windows clipboard expects CRLF for multiline plain text in many apps.
            string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                                    .Replace("\r", "\n", StringComparison.Ordinal)
                                    .Replace("\n", "\r\n", StringComparison.Ordinal);

            nuint bytes = (nuint)((normalized.Length + 1) * 2);
            nint hGlobal = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, bytes);
            if (hGlobal == 0)
            {
                return false;
            }

            nint ptr = NativeMethods.GlobalLock(hGlobal);
            if (ptr == 0)
            {
                NativeMethods.GlobalFree(hGlobal);
                return false;
            }

            try
            {
                Marshal.Copy(normalized.ToCharArray(), 0, ptr, normalized.Length);
                Marshal.WriteInt16(ptr, normalized.Length * 2, 0);
            }
            finally
            {
                NativeMethods.GlobalUnlock(hGlobal);
            }

            if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, hGlobal) == 0)
            {
                NativeMethods.GlobalFree(hGlobal);
                return false;
            }

            // Ownership of hGlobal transfers to the OS on SetClipboardData success.
            return true;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool TryOpenClipboardWithRetry()
    {
        for (int i = 0; i < 6; i++)
        {
            if (NativeMethods.OpenClipboard(0))
            {
                return true;
            }
            Thread.Sleep(8);
        }
        return false;
    }

    private static async Task<IReadOnlyDictionary<string, string>?> PromptUserAsync(
        TextExpansionRule rule,
        IReadOnlyList<ExpansionPromptField> fields)
    {
        var xamlRoot = App.MainWindowInstance?.Content?.XamlRoot;
        if (xamlRoot is null)
        {
            return null;
        }

        App.MainWindowInstance?.Activate();

        var panel = new StackPanel { Spacing = 10, MinWidth = 340 };
        var readers = new Dictionary<string, Func<string>>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            if (field.Kind == ExpansionPromptKind.Choice)
            {
                var combo = new ComboBox
                {
                    Header = field.Label,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    ItemsSource = field.Choices,
                    SelectedIndex = 0
                };
                panel.Children.Add(combo);
                readers[field.TokenKey] = () => combo.SelectedItem as string ?? field.DefaultValue;
            }
            else
            {
                var box = new TextBox
                {
                    Header = field.Label,
                    Text = field.DefaultValue
                };
                panel.Children.Add(box);
                readers[field.TokenKey] = () => box.Text ?? string.Empty;
            }
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = string.IsNullOrWhiteSpace(rule.Name) ? $"Expand \"{rule.Abbreviation}\"" : rule.Name,
            Content = panel,
            PrimaryButtonText = "Insert",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in readers)
        {
            result[kv.Key] = kv.Value();
        }
        return result;
    }
}
