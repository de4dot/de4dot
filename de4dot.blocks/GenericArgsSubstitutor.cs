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
using dnlib.Threading;

namespace de4dot.blocks {
	public struct GenericArgsSubstitutor {
		IList<TypeSig> genericArgs;
		IList<TypeSig> genericMethodArgs;
		bool updated;

		public static ITypeDefOrRef Create(ITypeDefOrRef type, GenericInstSig git) {
			if (git == null)
				return type;
			return Create(type, git.GenericArguments);
		}

		public static ITypeDefOrRef Create(ITypeDefOrRef type, IList<TypeSig> genericArgs) {
			if (genericArgs == null || genericArgs.Count == 0)
				return type;
			var ts = type as TypeSpec;
			if (ts == null)
				return type;
			var newSig = Create(ts.TypeSig, genericArgs);
			return newSig == ts.TypeSig ? type : new TypeSpecUser(newSig);
		}

		public static TypeSig Create(TypeSig type, IList<TypeSig> genericArgs) {
			if (type == null || genericArgs == null || genericArgs.Count == 0)
				return type;
			return new GenericArgsSubstitutor(genericArgs).Create(type);
		}

		public static TypeSig Create(TypeSig type, IList<TypeSig> genericArgs, IList<TypeSig> genericMethodArgs) {
			if (type == null || ((genericArgs == null || genericArgs.Count == 0) &&
				(genericMethodArgs == null || genericMethodArgs.Count == 0)))
				return type;
			return new GenericArgsSubstitutor(genericArgs, genericMethodArgs).Create(type);
		}

		public static IField Create(IField field, GenericInstSig git) {
			if (git == null)
				return field;
			return Create(field, git.GenericArguments);
		}

		public static IField Create(IField field, IList<TypeSig> genericArgs) {
			if (field == null || genericArgs == null || genericArgs.Count == 0)
				return field;
			var newSig = Create(field.FieldSig, genericArgs);
			if (newSig == field.FieldSig)
				return field;
			var module = field.DeclaringType != null ? field.DeclaringType.Module : null;
			return new MemberRefUser(module, field.Name, newSig, field.DeclaringType);
		}

		public static FieldSig Create(FieldSig sig, GenericInstSig git) {
			if (git == null)
				return sig;
			return Create(sig, git.GenericArguments);
		}

		public static FieldSig Create(FieldSig sig, IList<TypeSig> genericArgs) {
			if (sig == null || genericArgs == null || genericArgs.Count == 0)
				return sig;
			return new GenericArgsSubstitutor(genericArgs).Create(sig);
		}

		public static IMethod Create(IMethod method, GenericInstSig git) {
			if (git == null)
				return method;

			var mdr = method as IMethodDefOrRef;
			if (mdr != null)
				return Create(mdr, git);

			var ms = method as MethodSpec;
			if (ms != null)
				return Create(ms, git);

			return method;
		}

		public static MethodSpec Create(MethodSpec method, GenericInstSig git) {
			if (method == null || git == null)
				return method;
			var newMethod = Create(method.Method, git);
			var newInst = Create(method.GenericInstMethodSig, git);
			bool updated = newMethod != method.Method || newInst != method.GenericInstMethodSig;
			return updated ? new MethodSpecUser(newMethod, newInst) : method;
		}

		public static GenericInstMethodSig Create(GenericInstMethodSig sig, GenericInstSig git) {
			if (git == null)
				return sig;
			return Create(sig, git.GenericArguments);
		}

		public static GenericInstMethodSig Create(GenericInstMethodSig sig, IList<TypeSig> genericArgs) {
			if (sig == null || genericArgs == null || genericArgs.Count == 0)
				return sig;
			return new GenericArgsSubstitutor(genericArgs).Create(sig);
		}

		public static IMethodDefOrRef Create(IMethodDefOrRef method, GenericInstSig git) {
			if (git == null)
				return method;
			return Create(method, git.GenericArguments);
		}

		public static IMethodDefOrRef Create(IMethodDefOrRef method, IList<TypeSig> genericArgs) {
			return Create(method, genericArgs, null);
		}

		public static IMethodDefOrRef Create(IMethodDefOrRef method, GenericInstSig git, IList<TypeSig> genericMethodArgs) {
			return Create(method, git == null ? null : git.GenericArguments, genericMethodArgs);
		}

		// Creates a new method but keeps declaring type as is
		public static IMethodDefOrRef Create(IMethodDefOrRef method, IList<TypeSig> genericArgs, IList<TypeSig> genericMethodArgs) {
			if (method == null)
				return method;
			if ((genericArgs == null || genericArgs.Count == 0) && (genericMethodArgs == null || genericMethodArgs.Count == 0))
				return method;

			var sig = method.MethodSig;
			if (sig == null)
				return method;

			var newSig = new GenericArgsSubstitutor(genericArgs, genericMethodArgs).Create(sig);
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

		TypeSig Create(TypeSig type) {
			var newType = Create2(type);
			return updated ? newType : type;
		}

		TypeSig Create2(TypeSig type) {
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
				result = new PtrSig(Create2(type.Next));
				break;

			case ElementType.ByRef:
				result = new ByRefSig(Create2(type.Next));
				break;

			case ElementType.Array:
				var ary = (ArraySig)type;
				result = new ArraySig(ary.Next, ary.Rank, ary.Sizes, ary.LowerBounds);
				break;

			case ElementType.SZArray:
				result = new SZArraySig(Create2(type.Next));
				break;

			case ElementType.Pinned:
				result = new PinnedSig(Create2(type.Next));
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
				var newGis = new GenericInstSig(Create2(gis.GenericType) as ClassOrValueTypeSig, gis.GenericArguments.Count);
				for (int i = 0; i < gis.GenericArguments.Count; i++)
					newGis.GenericArguments.Add(Create2(gis.GenericArguments[i]));
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
				result = new FnPtrSig(Create(((FnPtrSig)type).MethodSig));
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

		MethodSig Create(MethodSig sig) {
			if (sig == null)
				return sig;
			var newSig = new MethodSig(sig.GetCallingConvention());
			newSig.RetType = Create2(sig.RetType);
			for (int i = 0; i < sig.Params.Count; i++)
				newSig.Params.Add(Create2(sig.Params[i]));
			newSig.GenParamCount = sig.GenParamCount;
			if (sig.ParamsAfterSentinel != null) {
				newSig.ParamsAfterSentinel = ThreadSafeListCreator.Create<TypeSig>();
				for (int i = 0; i < sig.ParamsAfterSentinel.Count; i++)
					newSig.ParamsAfterSentinel.Add(Create2(sig.ParamsAfterSentinel[i]));
			}
			return updated ? newSig : sig;
		}

		GenericInstMethodSig Create(GenericInstMethodSig sig) {
			var newSig = new GenericInstMethodSig();
			for (int i = 0; i < sig.GenericArguments.Count; i++)
				newSig.GenericArguments.Add(Create2(sig.GenericArguments[i]));
			return updated ? newSig : sig;
		}

		FieldSig Create(FieldSig sig) {
			var newSig = new FieldSig(Create2(sig.Type));
			return updated ? newSig : sig;
		}
	}
}
