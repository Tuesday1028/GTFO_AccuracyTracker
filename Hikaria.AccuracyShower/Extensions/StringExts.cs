using System.Text.RegularExpressions;

namespace Hikaria.AccuracyShower.Extensions
{
    internal static class StringExts
    {
        public static string RemoveHtmlTags(this string htmlString)
        {
            string pattern = "<.*?>";

            string plainText = Regex.Replace(htmlString, pattern, string.Empty);

            return plainText;
        }
    }
}
