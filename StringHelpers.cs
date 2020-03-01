using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace LuaExpose
{
    public static class StringExtensions
    {
        public static string FirstCharToUpper(this string input)
        {
            switch (input)
            {
                case null: throw new ArgumentNullException(nameof(input));
                case "": throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input));
                default: return input.First().ToString().ToUpper() + input.Substring(1);
            }
        }
        public static string FirstCharToLower(this string input)
        {
            switch (input)
            {
                case null: throw new ArgumentNullException(nameof(input));
                case "": throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input));
                default: return input.First().ToString().ToLower() + input.Substring(1);
            }
        }

        public static string GetTextBetween(this string value, string startTag, string endTag, StringComparison stringComparison = StringComparison.CurrentCulture)
        {
            if (!string.IsNullOrEmpty(value))
            {
                int startIndex = value.IndexOf(startTag, stringComparison) + startTag.Length;
                if (startIndex > -0)
                {
                    var endIndex = value.IndexOf(endTag, startIndex, stringComparison);
                    if (endIndex > 0)
                    {
                        return value.Substring(startIndex, endIndex - startIndex);
                    }
                }
            }
            return "";
        }
    }
}
