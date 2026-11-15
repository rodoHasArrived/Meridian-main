using System.Text;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Renders named string properties without interpreting braces inside property values.
/// With properties supplied, unknown placeholders remain visible and doubled braces
/// represent literal braces. Messages without properties retain their literal text.
/// </summary>
internal static class LogMessageTemplate
{
    public static string Render(string message, (string key, string value)[] properties)
    {
        if (properties.Length == 0)
            return message;

        var result = new StringBuilder(message.Length);
        for (var index = 0; index < message.Length; index++)
        {
            var current = message[index];
            if (index + 1 < message.Length &&
                ((current == '{' && message[index + 1] == '{') ||
                 (current == '}' && message[index + 1] == '}')))
            {
                result.Append(current);
                index++;
                continue;
            }

            if (current == '{')
            {
                var end = message.IndexOf('}', index + 1);
                if (end >= 0)
                {
                    var name = message.Substring(index + 1, end - index - 1);
                    var found = false;
                    foreach (var (key, value) in properties)
                    {
                        if (name != key)
                            continue;

                        result.Append(value);
                        index = end;
                        found = true;
                        break;
                    }

                    if (found)
                        continue;
                }
            }

            result.Append(current);
        }

        return result.ToString();
    }
}
