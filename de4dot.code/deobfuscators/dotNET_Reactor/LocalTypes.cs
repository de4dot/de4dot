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
using Mono.Cecil.Cil;

namespace de4dot.deobfuscators.dotNET_Reactor {
	class LocalTypes {
		Dictionary<string, int> localTypes = new Dictionary<string, int>(StringComparer.Ordinal);

		public IEnumerable<string> Types {
			get { return localTypes.Keys; }
		}

		public LocalTypes(MethodDefinition method) {
			if (method != null && method.Body != null)
				init(method.Body.Variables);
		}

		public LocalTypes(IEnumerable<VariableDefinition> locals) {
			init(locals);
		}

		void init(IEnumerable<VariableDefinition> locals) {
			if (locals == null)
				return;
			foreach (var local in locals) {
				var key = local.VariableType.FullName;
				int count;
				localTypes.TryGetValue(key, out count);
				localTypes[key] = count + 1;
			}
		}

		public bool exists(string typeFullName) {
			return localTypes.ContainsKey(typeFullName);
		}

		public bool all(IList<string> types) {
			foreach (var typeName in types) {
				if (!exists(typeName))
					return false;
			}
			return true;
		}

		public int count(string typeFullName) {
			int count;
			localTypes.TryGetValue(typeFullName, out count);
			return count;
		}
	}
}
