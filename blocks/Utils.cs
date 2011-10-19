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
		public static IDictionary<T, int> createObjectToIndexDictionary<T>(IList<T> objs) {
			var dict = new Dictionary<T, int>();
			for (int i = 0; i < objs.Count; i++)
				dict[objs[i]] = i;
			return dict;
		}

		public static List<TOut> convert<TIn, TOut>(IEnumerable<TIn> list) where TIn : TOut {
			var olist = new List<TOut>();
			foreach (var l in list)
				olist.Add(l);
			return olist;
		}

		public static IEnumerable<T> unique<T>(IEnumerable<T> values) {
			// HashSet is only available in .NET 3.5 and later.
			var dict = new Dictionary<T, bool>();
			foreach (var val in values)
				dict[val] = true;
			return dict.Keys;
		}
	}
}
