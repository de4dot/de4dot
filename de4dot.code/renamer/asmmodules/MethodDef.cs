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
	public class MMethodDef : Ref {
		IList<MGenericParamDef> genericParams;
		IList<MParamDef> paramDefs = new List<MParamDef>();
		MParamDef returnParamDef;
		int visibleParamCount;
		int visibleBaseIndex;

		public MPropertyDef Property { get; set; }
		public MEventDef Event { get; set; }

		public int VisibleParameterCount {
			get { return visibleParamCount; }
		}

		public int VisibleParameterBaseIndex {
			get { return visibleBaseIndex; }
		}

		public IList<MParamDef> ParamDefs {
			get { return paramDefs; }
		}

		public IEnumerable<MParamDef> AllParamDefs {
			get {
				yield return returnParamDef;
				foreach (var paramDef in paramDefs)
					yield return paramDef;
			}
		}

		public MParamDef ReturnParamDef {
			get { return returnParamDef; }
		}

		public IList<MGenericParamDef> GenericParams {
			get { return genericParams; }
		}

		public MethodDef MethodDef {
			get { return (MethodDef)memberRef; }
		}

		public MMethodDef(MethodDef methodDef, MTypeDef owner, int index)
			: base(methodDef, owner, index) {
			genericParams = MGenericParamDef.CreateGenericParamDefList(MethodDef.GenericParameters);
			visibleBaseIndex = methodDef.MethodSig != null && methodDef.MethodSig.HasThis ? 1 : 0;
			for (int i = 0; i < methodDef.Parameters.Count; i++) {
				var param = methodDef.Parameters[i];
				if (param.IsNormalMethodParameter)
					visibleParamCount++;
				paramDefs.Add(new MParamDef(param, i));
			}
			returnParamDef = new MParamDef(methodDef.Parameters.ReturnParameter, -1);
		}

		public bool IsPublic() {
			return MethodDef.IsPublic;
		}

		public bool IsVirtual() {
			return MethodDef.IsVirtual;
		}

		public bool IsNewSlot() {
			return MethodDef.IsNewSlot;
		}

		public bool IsStatic() {
			return MethodDef.IsStatic;
		}
	}
}
