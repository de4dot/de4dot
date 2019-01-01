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
using dnlib.DotNet;

namespace de4dot.code.deobfuscators {
	static class AllTypesHelper {
		public static IEnumerable<TypeDef> Types(IEnumerable<TypeDef> types) {
			var visited = new Dictionary<TypeDef, bool>();
			var stack = new Stack<IEnumerator<TypeDef>>();
			if (types != null)
				stack.Push(types.GetEnumerator());
			while (stack.Count > 0) {
				var enumerator = stack.Pop();
				while (enumerator.MoveNext()) {
					var type = enumerator.Current;
					if (visited.ContainsKey(type))
						continue;
					visited[type] = true;
					yield return type;
					if (type.NestedTypes.Count > 0) {
						stack.Push(enumerator);
						enumerator = type.NestedTypes.GetEnumerator();
					}
				}
			}
		}
	}
}
