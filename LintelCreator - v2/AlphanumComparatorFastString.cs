using System;
using System.Collections.Generic;
// 24.02.26 - изменено сравнение строк
namespace FerrumAddinDev
{
    public class AlphanumComparatorFastString : IComparer<string>
    {
        public int Compare(string s1, string s2)
        {
            if (ReferenceEquals(s1, s2)) return 0;
            if (s1 == null) return -1;
            if (s2 == null) return 1;

            int len1 = s1.Length;
            int len2 = s2.Length;
            int marker1 = 0;
            int marker2 = 0;

            while (marker1 < len1 && marker2 < len2)
            {
                char ch1 = s1[marker1];
                char ch2 = s2[marker2];

                // буферы под текущие "чанки"
                char[] space1 = new char[len1 - marker1];
                int loc1 = 0;
                char[] space2 = new char[len2 - marker2];
                int loc2 = 0;

                // собрать чанк 1
                bool isDigit1 = char.IsDigit(ch1);
                do
                {
                    space1[loc1++] = ch1;
                    marker1++;
                    if (marker1 >= len1) break;
                    ch1 = s1[marker1];
                } while (char.IsDigit(ch1) == isDigit1);

                // собрать чанк 2
                bool isDigit2 = char.IsDigit(ch2);
                do
                {
                    space2[loc2++] = ch2;
                    marker2++;
                    if (marker2 >= len2) break;
                    ch2 = s2[marker2];
                } while (char.IsDigit(ch2) == isDigit2);

                string str1 = new string(space1, 0, loc1);
                string str2 = new string(space2, 0, loc2);

                int result;

                // числа сравниваем численно
                if (isDigit1 && isDigit2)
                {
                    // чтобы "0012" и "12" сравнивались корректно:
                    // 1) по значению
                    // 2) при равенстве — по длине (короче раньше)
                    if (!long.TryParse(str1, out var n1)) n1 = 0;
                    if (!long.TryParse(str2, out var n2)) n2 = 0;

                    result = n1.CompareTo(n2);
                    if (result != 0) return result;

                    result = str1.Length.CompareTo(str2.Length);
                    if (result != 0) return result;
                }
                else
                {
                    result = string.Compare(str1, str2, StringComparison.OrdinalIgnoreCase);
                    if (result != 0) return result;

                    // Заглавные раньше строчных (Ordinal даёт 'Б' < 'б')
                    result = string.Compare(str1, str2, StringComparison.Ordinal);
                    if (result != 0) return result;
                }
            }

            // если всё совпало до конца одной строки
            return len1.CompareTo(len2);
        }
    }
}