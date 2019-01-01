/*
    Copyright (C) 2011-2015 de4dot@gmail.com

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
	public static class RandomNameChecker {
		static Regex noUpper = new Regex(@"^[^A-Z]+$");
		static Regex allUpper = new Regex(@"^[A-Z]+$");

		public static bool IsNonRandom(string name) {
			if (name.Length < 5)
				return true;
			if (noUpper.IsMatch(name))
				return true;
			if (allUpper.IsMatch(name))
				return true;

			for (int i = 0; i < name.Length - 1; i++) {
				if (IsDigit(name[i]))
					return false;
				if (i > 0 && IsUpper(name[i]) && IsUpper(name[i - 1]))
					return false;
			}

			var words = GetCamelWords(name);
			int vowels = 0;
			foreach (var word in words) {
				if (word.Length > 1 && HasVowel(word))
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

		static bool HasVowel(string s) {
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

		static List<string> GetCamelWords(string name) {
			var words = new List<string>();
			var sb = new StringBuilder();

			for (int i = 0; i < name.Length; i++) {
				char c = name[i];
				if (IsUpper(c)) {
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
		public static bool IsRandom(string name) {
			int len = name.Length;
			if (len < 5)
				return false;

			var typeWords = GetTypeWords(name);

			if (CountNumbers(typeWords, 2))
				return true;

			CountTypeWords(typeWords, out int lower, out int upper, out int digits);
			if (upper >= 3)
				return true;
			bool hasTwoUpperWords = upper == 2;

			foreach (var word in typeWords) {
				if (word.Length > 1 && IsDigit(word[0]))
					return true;
			}

			// Check for: lower, digit, lower
			for (int i = 2; i < typeWords.Count; i++) {
				if (IsDigit(typeWords[i - 1][0]) && IsLower(typeWords[i - 2][0]) && IsLower(typeWords[i][0]))
					return true;
			}

			if (hasTwoUpperWords && HasDigit(name))
				return true;

			// Check if it ends in lower, upper, digit
			if (IsLower(name[len - 3]) && IsUpper(name[len - 2]) && IsDigit(name[len - 1]))
				return true;

			return false;
		}

		static bool HasDigit(string s) {
			foreach (var c in s) {
				if (IsDigit(c))
					return true;
			}
			return false;
		}

		static List<string> GetTypeWords(string s) {
			var words = new List<string>();
			var sb = new StringBuilder();

			for (int i = 0; i < s.Length; ) {
				if (IsDigit(s[i])) {
					sb.Length = 0;
					while (i < s.Length && IsDigit(s[i]))
						sb.Append(s[i++]);
					words.Add(sb.ToString());
				}
				else if (IsUpper(s[i])) {
					sb.Length = 0;
					while (i < s.Length && IsUpper(s[i]))
						sb.Append(s[i++]);
					words.Add(sb.ToString());
				}
				else if (IsLower(s[i])) {
					sb.Length = 0;
					while (i < s.Length && IsLower(s[i]))
						sb.Append(s[i++]);
					words.Add(sb.ToString());
				}
				else {
					sb.Length = 0;
					while (i < s.Length) {
						if (IsDigit(s[i]) || IsUpper(s[i]) || IsLower(s[i]))
							break;
						sb.Append(s[i++]);
					}
					words.Add(sb.ToString());
				}
			}

			return words;
		}

		static bool CountNumbers(List<string> words, int numbers) {
			int num = 0;
			foreach (var word in words) {
				if (string.IsNullOrEmpty(word))
					continue;
				if (IsDigit(word[0]) && ++num >= numbers)
					return true;
			}
			return false;
		}

		// 2+ chars only
		static void CountTypeWords(List<string> words, out int lower, out int upper, out int digits) {
			lower = 0;
			upper = 0;
			digits = 0;

			foreach (var word in words) {
				if (word.Length <= 1)
					continue;
				char c = word[0];
				if (IsDigit(c))
					digits++;
				else if (IsLower(c))
					lower++;
				else if (IsUpper(c))
					upper++;
			}
		}

		static bool IsLower(char c) => 'a' <= c && c <= 'z';
		static bool IsUpper(char c) => 'A' <= c && c <= 'Z';
		static bool IsDigit(char c) => '0' <= c && c <= '9';
	}
}
