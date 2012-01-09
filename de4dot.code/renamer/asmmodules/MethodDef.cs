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
	class MethodDef : Ref {
		IList<GenericParamDef> genericParams;
		IList<ParamDef> paramDefs = new List<ParamDef>();

		public PropertyDef Property { get; set; }
		public EventDef Event { get; set; }

		public IList<ParamDef> ParamDefs {
			get { return paramDefs; }
		}

		public IList<GenericParamDef> GenericParams {
			get { return genericParams; }
		}

		public MethodDefinition MethodDefinition {
			get { return (MethodDefinition)memberReference; }
		}

		public MethodDef(MethodDefinition methodDefinition, TypeDef owner, int index)
			: base(methodDefinition, owner, index) {
			genericParams = GenericParamDef.createGenericParamDefList(MethodDefinition.GenericParameters);
			for (int i = 0; i < methodDefinition.Parameters.Count; i++) {
				var param = methodDefinition.Parameters[i];
				paramDefs.Add(new ParamDef(param, i));
			}
		}

		public bool isPublic() {
			return MethodDefinition.IsPublic;
		}

		public bool isVirtual() {
			return MethodDefinition.IsVirtual;
		}

		public bool isNewSlot() {
			return MethodDefinition.IsNewSlot;
		}

		public bool isStatic() {
			return MethodDefinition.IsStatic;
		}
	}
}
