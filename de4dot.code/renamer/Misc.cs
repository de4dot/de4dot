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
using Mono.Cecil;

namespace de4dot.renamer {
	interface IResolver {
		TypeDefinition resolve(TypeReference typeReference);
		MethodDefinition resolve(MethodReference methodReference);
		FieldDefinition resolve(FieldReference fieldReference);
	}

	interface IDefFinder {
		MethodDef findMethod(MethodReference methodReference);
		PropertyDef findProp(MethodReference methodReference);
		EventDef findEvent(MethodReference methodReference);
	}

	class MyDict<T> {
		Dictionary<string, T> dict = new Dictionary<string, T>(StringComparer.Ordinal);
		public T this[string key] {
			get {
				T t;
				if (dict.TryGetValue(key, out t))
					return t;
				return default(T);
			}
			set {
				dict[key] = value;
			}
		}

		public IEnumerable<T> getAll() {
			foreach (var elem in dict.Values)
				yield return elem;
		}
	}

	class DefDict<T> where T : Ref {
		MyDict<IList<T>> dict = new MyDict<IList<T>>();
		public Action<T, T> HandleDupe { get; set; }

		public IEnumerable<T> getAll() {
			foreach (var list in dict.getAll()) {
				foreach (var t in list)
					yield return t;
			}
		}

		public IEnumerable<T> getSorted() {
			var list = new List<T>(getAll());
			list.Sort((a, b) => {
				if (a.Index < b.Index) return -1;
				if (a.Index > b.Index) return 1;
				return 0;
			});
			return list;
		}

		public T find(MemberReference mr) {
			var list = dict[mr.Name];
			if (list == null)
				return null;

			foreach (T t in list) {
				if (t.isSame(mr))
					return t;
			}
			return null;
		}

		public void add(T t) {
			var other = find(t.MemberReference);
			if (other != null) {
				handleDupe(t, other);
				return;
			}

			var list = dict[t.MemberReference.Name];
			if (list == null)
				dict[t.MemberReference.Name] = list = new List<T>();
			list.Add(t);
			return;
		}

		public void replaceOldWithNew(T oldOne, T newOne) {
			if (find(newOne.MemberReference) != oldOne)
				throw new ApplicationException("Not same member reference");

			var list = dict[oldOne.MemberReference.Name];
			if (!list.Remove(oldOne))
				throw new ApplicationException("Could not remove old one");
			list.Add(newOne);
		}

		void handleDupe(T newOne, T oldOne) {
			Log.v("Duplicate MemberReference found: {0} ({1:X8} vs {2:X8})", newOne.MemberReference, newOne.MemberReference.MetadataToken.ToUInt32(), oldOne.MemberReference.MetadataToken.ToUInt32());
			if (HandleDupe != null)
				HandleDupe(newOne, oldOne);
		}
	}

	class Renamed {
		public string OldName { get; set; }
		public string NewName { get; set; }
	}
}
