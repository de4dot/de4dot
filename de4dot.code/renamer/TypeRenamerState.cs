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
using dnlib.DotNet;

namespace de4dot.code.renamer {
	public class TypeRenamerState {
		ExistingNames existingNames;
		Dictionary<string, string> namespaceToNewName;
		NameCreator createNamespaceName;
		public ITypeNameCreator globalTypeNameCreator;
		public ITypeNameCreator internalTypeNameCreator;

		public TypeRenamerState() {
			existingNames = new ExistingNames();
			namespaceToNewName = new Dictionary<string, string>(StringComparer.Ordinal);
			createNamespaceName = new NameCreator("ns");
			globalTypeNameCreator = new GlobalTypeNameCreator(existingNames);
			internalTypeNameCreator = new TypeNameCreator(existingNames);
		}

		public void AddTypeName(string name) {
			existingNames.Add(name);
		}

		public string GetTypeName(string oldName, string newName) {
			return existingNames.GetName(oldName, new NameCreator2(newName));
		}

		public string CreateNamespace(TypeDef type, string ns) {
			string newName;

			string asmFullName;
			if (type.Module.Assembly != null)
				asmFullName = type.Module.Assembly.FullName;
			else
				asmFullName = "<no assembly>";

			// Make sure that two namespaces with the same names in different modules aren't renamed
			// to the same name.
			var key = string.Format(" [{0}] [{1}] [{2}] [{3}] ",
						type.Module.Location,
						asmFullName,
						type.Module.Name,
						ns);
			if (namespaceToNewName.TryGetValue(key, out newName))
				return newName;
			return namespaceToNewName[key] = createNamespaceName.Create();
		}
	}
}
