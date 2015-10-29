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
using de4dot.code.renamer.asmmodules;

namespace de4dot.code.renamer {
	public class DerivedFrom {
		Dictionary<string, bool> classNames = new Dictionary<string, bool>(StringComparer.Ordinal);
		Dictionary<MTypeDef, bool> results = new Dictionary<MTypeDef, bool>();

		public DerivedFrom(string className) {
			AddName(className);
		}

		public DerivedFrom(string[] classNames) {
			foreach (var className in classNames)
				AddName(className);
		}

		void AddName(string className) {
			classNames[className] = true;
		}

		public bool Check(MTypeDef type) {
			return Check(type, 0);
		}

		public bool Check(MTypeDef type, int recurseCount) {
			if (recurseCount >= 100)
				return false;
			if (results.ContainsKey(type))
				return results[type];

			bool val;
			if (classNames.ContainsKey(type.TypeDef.FullName))
				val = true;
			else if (type.baseType == null) {
				if (type.TypeDef.BaseType != null)
					val = classNames.ContainsKey(type.TypeDef.BaseType.FullName);
				else
					val = false;
			}
			else
				val = Check(type.baseType.typeDef, recurseCount + 1);

			results[type] = val;
			return val;
		}
	}
}
