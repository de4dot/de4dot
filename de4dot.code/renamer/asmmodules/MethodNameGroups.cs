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

using System;
using System.Collections.Generic;

namespace de4dot.code.renamer.asmmodules {
	public class MethodNameGroup {
		List<MMethodDef> methods = new List<MMethodDef>();

		public List<MMethodDef> Methods => methods;
		public int Count => methods.Count;

		public void Add(MMethodDef method) => methods.Add(method);

		public void Merge(MethodNameGroup other) {
			if (this == other)
				return;
			methods.AddRange(other.methods);
		}

		public bool HasNonRenamableMethod() {
			foreach (var method in methods) {
				if (!method.Owner.HasModule)
					return true;
			}
			return false;
		}

		public bool HasInterfaceMethod() {
			foreach (var method in methods) {
				if (method.Owner.TypeDef.IsInterface)
					return true;
			}
			return false;
		}

		public bool HasGetterOrSetterPropertyMethod() {
			foreach (var method in methods) {
				if (method.Property == null)
					continue;
				var prop = method.Property;
				if (method == prop.GetMethod || method == prop.SetMethod)
					return true;
			}
			return false;
		}

		public bool HasAddRemoveOrRaiseEventMethod() {
			foreach (var method in methods) {
				if (method.Event == null)
					continue;
				var evt = method.Event;
				if (method == evt.AddMethod || method == evt.RemoveMethod || method == evt.RaiseMethod)
					return true;
			}
			return false;
		}

		public bool HasProperty() {
			foreach (var method in methods) {
				if (method.Property != null)
					return true;
			}
			return false;
		}

		public bool HasEvent() {
			foreach (var method in methods) {
				if (method.Event != null)
					return true;
			}
			return false;
		}

		public override string ToString() => $"{methods.Count} -- {(methods.Count > 0 ? methods[0].ToString() : "")}";
	}

	public class MethodNameGroups {
		Dictionary<MMethodDef, MethodNameGroup> methodGroups = new Dictionary<MMethodDef, MethodNameGroup>();

		public void Same(MMethodDef a, MMethodDef b) => Merge(Get(a), Get(b));
		public void Add(MMethodDef methodDef) => Get(methodDef);

		public MethodNameGroup Get(MMethodDef method) {
			if (!method.IsVirtual())
				throw new ApplicationException("Not a virtual method");
			if (!methodGroups.TryGetValue(method, out var group)) {
				methodGroups[method] = group = new MethodNameGroup();
				group.Add(method);
			}
			return group;
		}

		void Merge(MethodNameGroup a, MethodNameGroup b) {
			if (a == b)
				return;

			if (a.Count < b.Count) {
				var tmp = a;
				a = b;
				b = tmp;
			}
			a.Merge(b);
			foreach (var methodDef in b.Methods)
				methodGroups[methodDef] = a;
		}

		public IEnumerable<MethodNameGroup> GetAllGroups() => Utils.Unique(methodGroups.Values);
	}
}
