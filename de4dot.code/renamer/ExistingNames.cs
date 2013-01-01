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
using dnlib.DotNet;

namespace de4dot.code.renamer {
	class ExistingNames {
		Dictionary<string, bool> allNames = new Dictionary<string, bool>(StringComparer.Ordinal);

		public void add(string name) {
			allNames[name] = true;
		}

		public bool exists(string name) {
			return allNames.ContainsKey(name);
		}

		public string getName(UTF8String oldName, INameCreator nameCreator) {
			return getName(UTF8String.ToSystemStringOrEmpty(oldName), nameCreator);
		}

		public string getName(string oldName, INameCreator nameCreator) {
			return getName(oldName, () => nameCreator.create());
		}

		public string getName(UTF8String oldName, Func<string> createNewName) {
			return getName(UTF8String.ToSystemStringOrEmpty(oldName), createNewName);
		}

		public string getName(string oldName, Func<string> createNewName) {
			string prevName = null;
			while (true) {
				var name = createNewName();
				if (name == prevName)
					throw new ApplicationException(string.Format("Could not rename symbol to {0}", Utils.toCsharpString(name)));

				if (!exists(name) || name == oldName) {
					allNames[name] = true;
					return name;
				}

				prevName = name;
			}
		}

		public void merge(ExistingNames other) {
			foreach (var key in other.allNames.Keys)
				allNames[key] = true;
		}
	}
}
