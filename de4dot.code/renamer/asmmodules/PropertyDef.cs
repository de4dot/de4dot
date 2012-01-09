/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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

using System.Collections.Generic;
using Mono.Cecil;

namespace de4dot.code.renamer.asmmodules {
	class PropertyDef : Ref {
		public MethodDef GetMethod { get; set; }
		public MethodDef SetMethod { get; set; }

		public PropertyDefinition PropertyDefinition {
			get { return (PropertyDefinition)memberReference; }
		}

		public PropertyDef(PropertyDefinition propertyDefinition, TypeDef owner, int index)
			: base(propertyDefinition, owner, index) {
		}

		public IEnumerable<MethodDefinition> methodDefinitions() {
			if (PropertyDefinition.GetMethod != null)
				yield return PropertyDefinition.GetMethod;
			if (PropertyDefinition.SetMethod != null)
				yield return PropertyDefinition.SetMethod;
			if (PropertyDefinition.OtherMethods != null) {
				foreach (var m in PropertyDefinition.OtherMethods)
					yield return m;
			}
		}

		public bool isVirtual() {
			foreach (var method in methodDefinitions()) {
				if (method.IsVirtual)
					return true;
			}
			return false;
		}

		public bool isItemProperty() {
			if (GetMethod != null && GetMethod.ParamDefs.Count >= 1)
				return true;
			if (SetMethod != null && SetMethod.ParamDefs.Count >= 2)
				return true;
			return false;
		}
	}
}
