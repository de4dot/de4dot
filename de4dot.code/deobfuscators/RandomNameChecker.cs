/*
    Copyright (C) 2011-2012 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace de4dot.code.deobfuscators {
	static class RandomNameChecker {
		static Regex noUpper = new Regex(@"^[^A-Z]+$");
		static Regex allUpper = new Regex(@"^[A-Z]+$");

		public static bool isNonRandom(string name) {
			if (name.Length < 5)
				return true;
			if (noUpper.IsMatch(name))
				return true;
			if (allUpper.IsMatch(name))
				return true;

			for (int i = 0; i < name.Length - 1; i++) {
				if (isDigit(name[i]))
					return false;
				if (i > 0 && isUpper(name[i]) && isUpper(name[i - 1]))
					return false;
			}

			var words = getCamelWords(name);
			int vowels = 0;
			foreach (var word in words) {
				if (word.Length > 1 && hasVowel(word))
					vowels++;
			}
			switch (words.Count) {
			case 1:
				return vowels == words.Count;
			case 2:
			case 3:
				return vowels >= 1;
			case 4:
			case 5:
				return vowels >= 2;
			case 6:
				return vowels >= 3;
			case 7:
				return vowels >= 4;
			default:
				return vowels >= words.Count - 4;
			}
		}

		static bool hasVowel(string s) {
			foreach (var c in s) {
				switch (c) {
				case 'A':
				case 'a':
				case 'E':
				case 'e':
				case 'I':
				case 'i':
				case 'O':
				case 'o':
				case 'U':
				case 'u':
				case 'Y':
				case 'y':
					return true;
				}
			}
			return false;
		}

		static List<string> getCamelWords(string name) {
			var words = new List<string>();
			var sb = new StringBuilder();

			for (int i = 0; i < name.Length; i++) {
				char c = name[i];
				if (isUpper(c)) {
					if (sb.Length > 0)
						words.Add(sb.ToString());
					sb.Length = 0;
				}
				sb.Append(c);
			}
			if (sb.Length > 0)
				words.Add(sb.ToString());

			return words;
		}

		// Returns true if random, false if unknown
		public static bool isRandom(string name) {
			int len = name.Length;
			if (len < 5)
				return false;

			var typeWords = getTypeWords(name);

			if (countNumbers(typeWords, 2))
				return true;

			int lower, upper, digits;
			countTypeWords(typeWords, out lower, out upper, out digits);
			if (upper >= 3)
				return true;
			bool hasTwoUpperWords = upper == 2;

			foreach (var word in typeWords) {
				if (word.Length > 1 && isDigit(word[0]))
					return true;
			}

			// Check for: lower, digit, lower
			for (int i = 2; i < typeWords.Count; i++) {
				if (isDigit(typeWords[i - 1][0]) && isLower(typeWords[i - 2][0]) && isLower(typeWords[i][0]))
					return true;
			}

			if (hasTwoUpperWords && hasDigit(name))
				return true;

			// Check if it ends in lower, upper, digit
			if (isLower(name[len - 3]) && isUpper(name[len - 2]) && isDigit(name[len - 1]))
				return true;

			return false;
		}

		static bool hasDigit(string s) {
			foreach (var c in s) {
				if (isDigit(c))
					return true;
			}
			return false;
		}

		static List<string> getTypeWords(string s) {
			var words = new List<string>();
			var sb = new StringBuilder();

			for (int i = 0; i < s.Length; ) {
				if (isDigit(s[i])) {
					sb.Length = 0;
					while (i < s.Length && isDigit(s[i]))
						sb.Append(s[i++]);
					words.Add(sb.ToString());
				}
				else if (isUpper(s[i])) {
					sb.Length = 0;
					while (i < s.Length && isUpper(s[i]))
						sb.Append(s[i++]);
					words.Add(sb.ToString());
				}
				else if (isLower(s[i])) {
					sb.Length = 0;
					while (i < s.Length && isLower(s[i]))
						sb.Append(s[i++]);
					words.Add(sb.ToString());
				}
				else {
					sb.Length = 0;
					while (i < s.Length) {
						if (isDigit(s[i]) || isUpper(s[i]) || isLower(s[i]))
							break;
						sb.Append(s[i++]);
					}
					words.Add(sb.ToString());
				}
			}

			return words;
		}

		static bool countNumbers(List<string> words, int numbers) {
			int num = 0;
			foreach (var word in words) {
				if (string.IsNullOrEmpty(word))
					continue;
				if (isDigit(word[0]) && ++num >= numbers)
					return true;
			}
			return false;
		}

		// 2+ chars only
		static void countTypeWords(List<string> words, out int lower, out int upper, out int digits) {
			lower = 0;
			upper = 0;
			digits = 0;

			foreach (var word in words) {
				if (word.Length <= 1)
					continue;
				char c = word[0];
				if (isDigit(c))
					digits++;
				else if (isLower(c))
					lower++;
				else if (isUpper(c))
					upper++;
			}
		}

		static bool isLower(char c) {
			return 'a' <= c && c <= 'z';
		}

		static bool isUpper(char c) {
			return 'A' <= c && c <= 'Z';
		}

		static bool isDigit(char c) {
			return '0' <= c && c <= '9';
		}
	}
}
