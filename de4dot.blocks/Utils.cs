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

namespace de4dot.blocks {
	internal delegate TResult Func<TResult>();
	internal delegate TResult Func<T, TResult>(T arg);
	internal delegate TResult Func<T1, T2, TResult>(T1 arg1, T2 arg2);
	internal delegate TResult Func<T1, T2, T3, TResult>(T1 arg1, T2 arg2, T3 arg3);
	internal delegate void Action();
	internal delegate void Action<T>(T arg);
	internal delegate void Action<T1, T2>(T1 arg1, T2 arg2);
	internal delegate void Action<T1, T2, T3>(T1 arg1, T2 arg2, T3 arg3);

	public class Tuple<T1, T2> {
		public T1 Item1 { get; set; }
		public T2 Item2 { get; set; }
		public override bool Equals(object obj) {
			var other = obj as Tuple<T1, T2>;
			if (other == null)
				return false;
			return Item1.Equals(other.Item1) && Item2.Equals(other.Item2);
		}
		public override int GetHashCode() {
			return Item1.GetHashCode() + Item2.GetHashCode();
		}
		public override string ToString() {
			return "<" + Item1.ToString() + "," + Item2.ToString() + ">";
		}
	}

	static class Utils {
		public static IDictionary<T, int> CreateObjectToIndexDictionary<T>(IList<T> objs) {
			var dict = new Dictionary<T, int>();
			for (int i = 0; i < objs.Count; i++)
				dict[objs[i]] = i;
			return dict;
		}

		public static List<TOut> Convert<TIn, TOut>(IEnumerable<TIn> list) where TIn : TOut {
			var olist = new List<TOut>();
			foreach (var l in list)
				olist.Add(l);
			return olist;
		}

		public static IEnumerable<T> Unique<T>(IEnumerable<T> values) {
			// HashSet is only available in .NET 3.5 and later.
			var dict = new Dictionary<T, bool>();
			foreach (var val in values)
				dict[val] = true;
			return dict.Keys;
		}
	}
}
