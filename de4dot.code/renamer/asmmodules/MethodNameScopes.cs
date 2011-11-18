/*
    Copyright (C) 2011 de4dot@gmail.com

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

using System;
using System.Collections.Generic;

namespace de4dot.renamer.asmmodules {
	class MethodNameScope {
		List<MethodDef> methods = new List<MethodDef>();

		public List<MethodDef> Methods {
			get { return methods; }
		}

		public int Count {
			get { return methods.Count; }
		}

		public void add(MethodDef method) {
			methods.Add(method);
		}

		public void merge(MethodNameScope other) {
			if (this == other)
				return;
			methods.AddRange(other.methods);
		}

		public override string ToString() {
			return string.Format("{0} -- {1}", methods.Count, methods.Count > 0 ? methods[0].ToString() : "");
		}
	}

	class MethodNameScopes {
		Dictionary<MethodDef, MethodNameScope> methodScopes = new Dictionary<MethodDef, MethodNameScope>();

		public void same(MethodDef a, MethodDef b) {
			merge(get(a), get(b));
		}

		public void add(MethodDef methodDef) {
			get(methodDef);
		}

		MethodNameScope get(MethodDef method) {
			if (!method.isVirtual())
				throw new ApplicationException("Not a virtual method");
			MethodNameScope scope;
			if (!methodScopes.TryGetValue(method, out scope)) {
				methodScopes[method] = scope = new MethodNameScope();
				scope.add(method);
			}
			return scope;
		}

		void merge(MethodNameScope a, MethodNameScope b) {
			if (a == b)
				return;

			if (a.Count < b.Count) {
				MethodNameScope tmp = a;
				a = b;
				b = tmp;
			}
			a.merge(b);
			foreach (var methodDef in b.Methods)
				methodScopes[methodDef] = a;
		}

		public IEnumerable<MethodNameScope> getAllScopes() {
			return Utils.unique(methodScopes.Values);
		}
	}
}
