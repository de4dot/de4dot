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
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	// Converts type references/definitions in one module to this module
	class MemberRefConverter {
		ModuleDefMD module;

		public ModuleDefMD Module => module;
		public MemberRefConverter(ModuleDefMD module) => this.module = module;

		bool IsInOurModule(IMemberRef memberRef) => memberRef.Module == module;
		Importer CreateImporter() => new Importer(module, ImporterOptions.TryToUseTypeDefs);
		public TypeSig Convert(TypeRef typeRef) => CreateImporter().Import(typeRef).ToTypeSig();
		ITypeDefOrRef Convert(ITypeDefOrRef tdr) => (ITypeDefOrRef)CreateImporter().Import(tdr);
		TypeSig Convert2(TypeSig ts) => CreateImporter().Import(ts);
		public TypeSig Convert(TypeSig ts) => CreateImporter().Import(ts);

		public IField Convert(IField fieldRef) {
			if (IsInOurModule(fieldRef))
				return TryGetFieldDef(fieldRef);
			return CreateImporter().Import(fieldRef);
		}

		public IMethodDefOrRef Convert(IMethod methodRef) {
			if (!(methodRef is MemberRef || methodRef is MethodDef) || methodRef.MethodSig == null)
				throw new ApplicationException("Invalid method reference type");
			if (IsInOurModule(methodRef))
				return (IMethodDefOrRef)TryGetMethodDef(methodRef);
			return (IMethodDefOrRef)CreateImporter().Import(methodRef);
		}

		public IField TryGetFieldDef(IField fieldRef) {
			if (fieldRef is FieldDef fieldDef)
				return fieldDef;

			var declaringType = DotNetUtils.GetType(module, fieldRef.DeclaringType);
			if (declaringType == null)
				return fieldRef;
			return DotNetUtils.GetField(declaringType, fieldRef);
		}

		public IMethod TryGetMethodDef(IMethod methodRef) {
			if (methodRef is MethodDef methodDef)
				return methodDef;

			var declaringType = DotNetUtils.GetType(module, methodRef.DeclaringType);
			if (declaringType == null)
				return methodRef;
			return DotNetUtils.GetMethod(declaringType, methodRef);
		}
	}
}
