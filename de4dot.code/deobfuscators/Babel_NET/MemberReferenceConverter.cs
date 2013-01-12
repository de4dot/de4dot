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

using System;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	// Converts type references/definitions in one module to this module
	class MemberRefConverter {
		ModuleDefMD module;

		public ModuleDefMD Module {
			get { return module; }
		}

		public MemberRefConverter(ModuleDefMD module) {
			this.module = module;
		}

		bool isInOurModule(IMemberRef memberRef) {
			return memberRef.Module == module;
		}

		Importer createImporter() {
			return new Importer(module, ImporterOptions.TryToUseTypeDefs);
		}

		public TypeSig convert(TypeRef typeRef) {
			return createImporter().Import(typeRef).ToTypeSig();
		}

		ITypeDefOrRef convert(ITypeDefOrRef tdr) {
			return (ITypeDefOrRef)createImporter().Import(tdr);
		}

		TypeSig convert2(TypeSig ts) {
			return createImporter().Import(ts);
		}

		public TypeSig convert(TypeSig ts) {
			return createImporter().Import(ts);
		}

		public IField convert(IField fieldRef) {
			if (isInOurModule(fieldRef))
				return tryGetFieldDef(fieldRef);
			return createImporter().Import(fieldRef);
		}

		public IMethodDefOrRef convert(IMethod methodRef) {
			if (!(methodRef is MemberRef || methodRef is MethodDef) || methodRef.MethodSig == null)
				throw new ApplicationException("Invalid method reference type");
			if (isInOurModule(methodRef))
				return (IMethodDefOrRef)tryGetMethodDef(methodRef);
			return (IMethodDefOrRef)createImporter().Import(methodRef);
		}

		public IField tryGetFieldDef(IField fieldRef) {
			var fieldDef = fieldRef as FieldDef;
			if (fieldDef != null)
				return fieldDef;

			var declaringType = DotNetUtils.getType(module, fieldRef.DeclaringType);
			if (declaringType == null)
				return fieldRef;
			return DotNetUtils.getField(declaringType, fieldRef);
		}

		public IMethod tryGetMethodDef(IMethod methodRef) {
			var methodDef = methodRef as MethodDef;
			if (methodDef != null)
				return methodDef;

			var declaringType = DotNetUtils.getType(module, methodRef.DeclaringType);
			if (declaringType == null)
				return methodRef;
			return DotNetUtils.getMethod(declaringType, methodRef);
		}
	}
}
