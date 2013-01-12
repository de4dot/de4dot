/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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

namespace de4dot.code.renamer.asmmodules {
	class MethodNameGroup {
		List<MMethodDef> methods = new List<MMethodDef>();

		public List<MMethodDef> Methods {
			get { return methods; }
		}

		public int Count {
			get { return methods.Count; }
		}

		public void add(MMethodDef method) {
			methods.Add(method);
		}

		public void merge(MethodNameGroup other) {
			if (this == other)
				return;
			methods.AddRange(other.methods);
		}

		public bool hasNonRenamableMethod() {
			foreach (var method in methods) {
				if (!method.Owner.HasModule)
					return true;
			}
			return false;
		}

		public bool hasInterfaceMethod() {
			foreach (var method in methods) {
				if (method.Owner.TypeDef.IsInterface)
					return true;
			}
			return false;
		}

		public bool hasGetterOrSetterPropertyMethod() {
			foreach (var method in methods) {
				if (method.Property == null)
					continue;
				var prop = method.Property;
				if (method == prop.GetMethod || method == prop.SetMethod)
					return true;
			}
			return false;
		}

		public bool hasAddRemoveOrRaiseEventMethod() {
			foreach (var method in methods) {
				if (method.Event == null)
					continue;
				var evt = method.Event;
				if (method == evt.AddMethod || method == evt.RemoveMethod || method == evt.RaiseMethod)
					return true;
			}
			return false;
		}

		public bool hasProperty() {
			foreach (var method in methods) {
				if (method.Property != null)
					return true;
			}
			return false;
		}

		public bool hasEvent() {
			foreach (var method in methods) {
				if (method.Event != null)
					return true;
			}
			return false;
		}

		public override string ToString() {
			return string.Format("{0} -- {1}", methods.Count, methods.Count > 0 ? methods[0].ToString() : "");
		}
	}

	class MethodNameGroups {
		Dictionary<MMethodDef, MethodNameGroup> methodGroups = new Dictionary<MMethodDef, MethodNameGroup>();

		public void same(MMethodDef a, MMethodDef b) {
			merge(get(a), get(b));
		}

		public void add(MMethodDef methodDef) {
			get(methodDef);
		}

		public MethodNameGroup get(MMethodDef method) {
			if (!method.isVirtual())
				throw new ApplicationException("Not a virtual method");
			MethodNameGroup group;
			if (!methodGroups.TryGetValue(method, out group)) {
				methodGroups[method] = group = new MethodNameGroup();
				group.add(method);
			}
			return group;
		}

		void merge(MethodNameGroup a, MethodNameGroup b) {
			if (a == b)
				return;

			if (a.Count < b.Count) {
				MethodNameGroup tmp = a;
				a = b;
				b = tmp;
			}
			a.merge(b);
			foreach (var methodDef in b.Methods)
				methodGroups[methodDef] = a;
		}

		public IEnumerable<MethodNameGroup> getAllGroups() {
			return Utils.unique(methodGroups.Values);
		}
	}
}
