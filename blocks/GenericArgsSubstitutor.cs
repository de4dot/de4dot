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

using System.Collections.Generic;
using dnlib.DotNet;

namespace de4dot.blocks {
	public struct GenericArgsSubstitutor {
		IList<TypeSig> genericArgs;
		IList<TypeSig> genericMethodArgs;
		bool updated;

		public static ITypeDefOrRef create(ITypeDefOrRef type, GenericInstSig git) {
			if (git == null)
				return type;
			return create(type, git.GenericArguments);
		}

		public static ITypeDefOrRef create(ITypeDefOrRef type, IList<TypeSig> genericArgs) {
			if (genericArgs == null || genericArgs.Count == 0)
				return type;
			var ts = type as TypeSpec;
			if (ts == null)
				return type;
			var newSig = create(ts.TypeSig, genericArgs);
			return newSig == ts.TypeSig ? type : new TypeSpecUser(newSig);
		}

		public static TypeSig create(TypeSig type, IList<TypeSig> genericArgs) {
			if (type == null || genericArgs == null || genericArgs.Count == 0)
				return type;
			return new GenericArgsSubstitutor(genericArgs).create(type);
		}

		public static TypeSig create(TypeSig type, IList<TypeSig> genericArgs, IList<TypeSig> genericMethodArgs) {
			if (type == null || ((genericArgs == null || genericArgs.Count == 0) &&
				(genericMethodArgs == null || genericMethodArgs.Count == 0)))
				return type;
			return new GenericArgsSubstitutor(genericArgs, genericMethodArgs).create(type);
		}

		public static IField create(IField field, GenericInstSig git) {
			if (git == null)
				return field;
			return create(field, git.GenericArguments);
		}

		public static IField create(IField field, IList<TypeSig> genericArgs) {
			if (field == null || genericArgs == null || genericArgs.Count == 0)
				return field;
			var newSig = create(field.FieldSig, genericArgs);
			if (newSig == field.FieldSig)
				return field;
			var module = field.DeclaringType != null ? field.DeclaringType.Module : null;
			return new MemberRefUser(module, field.Name, newSig, field.DeclaringType);
		}

		public static FieldSig create(FieldSig sig, GenericInstSig git) {
			if (git == null)
				return sig;
			return create(sig, git.GenericArguments);
		}

		public static FieldSig create(FieldSig sig, IList<TypeSig> genericArgs) {
			if (sig == null || genericArgs == null || genericArgs.Count == 0)
				return sig;
			return new GenericArgsSubstitutor(genericArgs).create(sig);
		}

		public static IMethod create(IMethod method, GenericInstSig git) {
			if (git == null)
				return method;

			var mdr = method as IMethodDefOrRef;
			if (mdr != null)
				return create(mdr, git);

			var ms = method as MethodSpec;
			if (ms != null)
				return create(ms, git);

			return method;
		}

		public static MethodSpec create(MethodSpec method, GenericInstSig git) {
			if (method == null || git == null)
				return method;
			var newMethod = create(method.Method, git);
			var newInst = create(method.GenericInstMethodSig, git);
			bool updated = newMethod != method.Method || newInst != method.GenericInstMethodSig;
			return updated ? new MethodSpecUser(newMethod, newInst) : method;
		}

		public static GenericInstMethodSig create(GenericInstMethodSig sig, GenericInstSig git) {
			if (git == null)
				return sig;
			return create(sig, git.GenericArguments);
		}

		public static GenericInstMethodSig create(GenericInstMethodSig sig, IList<TypeSig> genericArgs) {
			if (sig == null || genericArgs == null || genericArgs.Count == 0)
				return sig;
			return new GenericArgsSubstitutor(genericArgs).create(sig);
		}

		public static IMethodDefOrRef create(IMethodDefOrRef method, GenericInstSig git) {
			if (git == null)
				return method;
			return create(method, git.GenericArguments);
		}

		public static IMethodDefOrRef create(IMethodDefOrRef method, IList<TypeSig> genericArgs) {
			return create(method, genericArgs, null);
		}

		public static IMethodDefOrRef create(IMethodDefOrRef method, GenericInstSig git, IList<TypeSig> genericMethodArgs) {
			return create(method, git == null ? null : git.GenericArguments, genericMethodArgs);
		}

		// Creates a new method but keeps declaring type as is
		public static IMethodDefOrRef create(IMethodDefOrRef method, IList<TypeSig> genericArgs, IList<TypeSig> genericMethodArgs) {
			if (method == null)
				return method;
			if ((genericArgs == null || genericArgs.Count == 0) && (genericMethodArgs == null || genericMethodArgs.Count == 0))
				return method;

			var sig = method.MethodSig;
			if (sig == null)
				return method;

			var newSig = new GenericArgsSubstitutor(genericArgs, genericMethodArgs).create(sig);
			if (newSig == sig)
				return method;

			return new MemberRefUser(method.DeclaringType.Module, method.Name, newSig, method.DeclaringType);
		}

		GenericArgsSubstitutor(IList<TypeSig> genericArgs) {
			this.genericArgs = genericArgs;
			this.genericMethodArgs = null;
			this.updated = false;
		}

		GenericArgsSubstitutor(IList<TypeSig> genericArgs, IList<TypeSig> genericMethodArgs) {
			this.genericArgs = genericArgs;
			this.genericMethodArgs = genericMethodArgs;
			this.updated = false;
		}

		TypeSig create(TypeSig type) {
			var newType = create2(type);
			return updated ? newType : type;
		}

		TypeSig create2(TypeSig type) {
			if (type == null)
				return type;
			TypeSig result;

			GenericSig varSig;
			switch (type.ElementType) {
			case ElementType.Void:
			case ElementType.Boolean:
			case ElementType.Char:
			case ElementType.I1:
			case ElementType.U1:
			case ElementType.I2:
			case ElementType.U2:
			case ElementType.I4:
			case ElementType.U4:
			case ElementType.I8:
			case ElementType.U8:
			case ElementType.R4:
			case ElementType.R8:
			case ElementType.String:
			case ElementType.TypedByRef:
			case ElementType.I:
			case ElementType.U:
			case ElementType.Object:
				result = type;
				break;

			case ElementType.Ptr:
				result = new PtrSig(create2(type.Next));
				break;

			case ElementType.ByRef:
				result = new ByRefSig(create2(type.Next));
				break;

			case ElementType.Array:
				var ary = (ArraySig)type;
				result = new ArraySig(ary.Next, ary.Rank, ary.Sizes, ary.LowerBounds);
				break;

			case ElementType.SZArray:
				result = new SZArraySig(create2(type.Next));
				break;

			case ElementType.Pinned:
				result = new PinnedSig(create2(type.Next));
				break;

			case ElementType.ValueType:
			case ElementType.Class:
				result = type;
				break;

			case ElementType.Var:
				varSig = (GenericSig)type;
				if (genericArgs != null && varSig.Number < (uint)genericArgs.Count) {
					result = genericArgs[(int)varSig.Number];
					updated = true;
				}
				else
					result = type;
				break;

			case ElementType.MVar:
				varSig = (GenericSig)type;
				if (genericMethodArgs != null && varSig.Number < (uint)genericMethodArgs.Count) {
					result = genericMethodArgs[(int)varSig.Number];
					updated = true;
				}
				else
					result = type;
				break;

			case ElementType.GenericInst:
				var gis = (GenericInstSig)type;
				var newGis = new GenericInstSig(create2(gis.GenericType) as ClassOrValueTypeSig, gis.GenericArguments.Count);
				for (int i = 0; i < gis.GenericArguments.Count; i++)
					newGis.GenericArguments.Add(create2(gis.GenericArguments[i]));
				result = newGis;
				break;

			case ElementType.ValueArray:
				result = new ValueArraySig(type.Next, ((ValueArraySig)type).Size);
				break;

			case ElementType.Module:
				result = new ModuleSig(((ModuleSig)type).Index, type.Next);
				break;

			case ElementType.CModReqd:
				result = new CModReqdSig(((ModifierSig)type).Modifier, type.Next);
				break;

			case ElementType.CModOpt:
				result = new CModOptSig(((ModifierSig)type).Modifier, type.Next);
				break;

			case ElementType.FnPtr:
				result = new FnPtrSig(create(((FnPtrSig)type).MethodSig));
				break;

			case ElementType.End:
			case ElementType.R:
			case ElementType.Sentinel:
			case ElementType.Internal:
			default:
				result = type;
				break;
			}

			return result;
		}

		MethodSig create(MethodSig sig) {
			if (sig == null)
				return sig;
			var newSig = new MethodSig(sig.GetCallingConvention());
			newSig.RetType = create2(sig.RetType);
			for (int i = 0; i < sig.Params.Count; i++)
				newSig.Params.Add(create2(sig.Params[i]));
			newSig.GenParamCount = sig.GenParamCount;
			if (sig.ParamsAfterSentinel != null) {
				newSig.ParamsAfterSentinel = new List<TypeSig>();
				for (int i = 0; i < sig.ParamsAfterSentinel.Count; i++)
					newSig.ParamsAfterSentinel.Add(create2(sig.ParamsAfterSentinel[i]));
			}
			return updated ? newSig : sig;
		}

		GenericInstMethodSig create(GenericInstMethodSig sig) {
			var newSig = new GenericInstMethodSig();
			for (int i = 0; i < sig.GenericArguments.Count; i++)
				newSig.GenericArguments.Add(create2(sig.GenericArguments[i]));
			return updated ? newSig : sig;
		}

		FieldSig create(FieldSig sig) {
			var newSig = new FieldSig(create2(sig.Type));
			return updated ? newSig : sig;
		}
	}
}
