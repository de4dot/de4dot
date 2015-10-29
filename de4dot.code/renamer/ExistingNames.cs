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
	public class ExistingNames {
		Dictionary<string, bool> allNames = new Dictionary<string, bool>(StringComparer.Ordinal);

		public void Add(string name) {
			allNames[name] = true;
		}

		public bool Exists(string name) {
			return allNames.ContainsKey(name);
		}

		public string GetName(UTF8String oldName, INameCreator nameCreator) {
			return GetName(UTF8String.ToSystemStringOrEmpty(oldName), nameCreator);
		}

		public string GetName(string oldName, INameCreator nameCreator) {
			return GetName(oldName, () => nameCreator.Create());
		}

		public string GetName(UTF8String oldName, Func<string> createNewName) {
			return GetName(UTF8String.ToSystemStringOrEmpty(oldName), createNewName);
		}

		public string GetName(string oldName, Func<string> createNewName) {
			string prevName = null;
			while (true) {
				var name = createNewName();
				if (name == prevName)
					throw new ApplicationException(string.Format("Could not rename symbol to {0}", Utils.ToCsharpString(name)));

				if (!Exists(name) || name == oldName) {
					allNames[name] = true;
					return name;
				}

				prevName = name;
			}
		}

		public void Merge(ExistingNames other) {
			if (this == other)
				return;
			foreach (var key in other.allNames.Keys)
				allNames[key] = true;
		}
	}
}
