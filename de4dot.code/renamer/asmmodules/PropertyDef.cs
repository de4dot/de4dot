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

using System.Collections.Generic;
using dnlib.DotNet;

namespace de4dot.code.renamer.asmmodules {
	public class MPropertyDef : Ref {
		public MMethodDef GetMethod { get; set; }
		public MMethodDef SetMethod { get; set; }

		public PropertyDef PropertyDef {
			get { return (PropertyDef)memberRef; }
		}

		public MPropertyDef(PropertyDef propertyDef, MTypeDef owner, int index)
			: base(propertyDef, owner, index) {
		}

		public IEnumerable<MethodDef> MethodDefs() {
			if (PropertyDef.GetMethod != null)
				yield return PropertyDef.GetMethod;
			if (PropertyDef.SetMethod != null)
				yield return PropertyDef.SetMethod;
			if (PropertyDef.OtherMethods != null) {
				foreach (var m in PropertyDef.OtherMethods)
					yield return m;
			}
		}

		public bool IsVirtual() {
			foreach (var method in MethodDefs()) {
				if (method.IsVirtual)
					return true;
			}
			return false;
		}

		public bool IsItemProperty() {
			if (GetMethod != null && GetMethod.VisibleParameterCount >= 1)
				return true;
			if (SetMethod != null && SetMethod.VisibleParameterCount >= 2)
				return true;
			return false;
		}
	}
}
