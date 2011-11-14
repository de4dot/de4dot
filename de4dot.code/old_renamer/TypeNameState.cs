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
using de4dot.deobfuscators;

namespace de4dot.old_renamer {
	class TypeNameState {
		public CurrentNames currentNames;
		IDictionary<string, string> namespaceToNewName;
		INameCreator createNamespaceName;
		public ITypeNameCreator globalTypeNameCreator;
		public ITypeNameCreator internalTypeNameCreator;
		Func<string, bool> isValidName;

		public Func<string, bool> IsValidName {
			get { return isValidName; }
			set { isValidName = value; }
		}

		public TypeNameState() {
			currentNames = new CurrentNames();
			namespaceToNewName = new Dictionary<string, string>(StringComparer.Ordinal);
			createNamespaceName = new GlobalNameCreator(new NameCreator("ns"));
			globalTypeNameCreator = new GlobalTypeNameCreator(currentNames);
			internalTypeNameCreator = new TypeNameCreator(currentNames);
		}

		public bool isValidNamespace(string ns) {
			foreach (var part in ns.Split(new char[] { '.' })) {
				if (!isValidName(part))
					return false;
			}
			return true;
		}

		public string newNamespace(string ns) {
			string newName;
			if (namespaceToNewName.TryGetValue(ns, out newName))
				return newName;
			return namespaceToNewName[ns] = createNamespaceName.newName();
		}
	}
}
