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

using System;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	class TypeReferenceConverter : TypeReferenceUpdaterBase  {
		MemberReferenceConverter memberReferenceConverter;

		ModuleDefinition Module {
			get { return memberReferenceConverter.Module; }
		}

		public TypeReferenceConverter(MemberReferenceConverter memberReferenceConverter) {
			this.memberReferenceConverter = memberReferenceConverter;
		}

		public TypeReference convert(TypeReference a) {
			var newOne = update(a);
			if (!(a is GenericParameter) && !MemberReferenceHelper.compareTypes(newOne, a))
				throw new ApplicationException("Could not convert type reference");
			return newOne;
		}

		protected override TypeReference updateTypeReference(TypeReference a) {
			if (a.Module == Module)
				return a;

			var newTypeRef = new TypeReference(a.Namespace, a.Name, Module, memberReferenceConverter.convert(a.Scope), a.IsValueType);
			foreach (var gp in a.GenericParameters)
				newTypeRef.GenericParameters.Add(new GenericParameter(gp.Name, newTypeRef));
			newTypeRef.DeclaringType = update(a.DeclaringType);
			newTypeRef.UpdateElementType();
			return newTypeRef;
		}
	}

	// Converts type references/definitions in one module to this module
	class MemberReferenceConverter {
		ModuleDefinition module;

		public ModuleDefinition Module {
			get { return module; }
		}

		public MemberReferenceConverter(ModuleDefinition module) {
			this.module = module;
		}

		bool isInOurModule(MemberReference memberRef) {
			return memberRef.Module == module;
		}

		public TypeReference convert(TypeReference typeRef) {
			if (typeRef == null)
				return null;
			typeRef = new TypeReferenceConverter(this).convert(typeRef);
			return tryGetTypeDefinition(typeRef);
		}

		public FieldReference convert(FieldReference fieldRef) {
			if (isInOurModule(fieldRef))
				return tryGetFieldDefinition(fieldRef);

			return new FieldReference(fieldRef.Name, convert(fieldRef.FieldType), convert(fieldRef.DeclaringType));
		}

		public MethodReference convert(MethodReference methodRef) {
			if (methodRef.GetType() != typeof(MethodReference) && methodRef.GetType() != typeof(MethodDefinition))
				throw new ApplicationException("Invalid method reference type");
			if (isInOurModule(methodRef))
				return tryGetMethodDefinition(methodRef);

			return copy(methodRef);
		}

		public MethodReference copy(MethodReference methodRef) {
			if (methodRef.GetType() != typeof(MethodReference) && methodRef.GetType() != typeof(MethodDefinition))
				throw new ApplicationException("Invalid method reference type");

			var newMethodRef = new MethodReference(methodRef.Name, convert(methodRef.MethodReturnType.ReturnType), convert(methodRef.DeclaringType));
			newMethodRef.HasThis = methodRef.HasThis;
			newMethodRef.ExplicitThis = methodRef.ExplicitThis;
			newMethodRef.CallingConvention = methodRef.CallingConvention;
			foreach (var param in methodRef.Parameters)
				newMethodRef.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes, convert(param.ParameterType)));
			foreach (var gp in methodRef.GenericParameters)
				newMethodRef.GenericParameters.Add(new GenericParameter(gp.Name, newMethodRef));
			return newMethodRef;
		}

		public IMetadataScope convert(IMetadataScope scope) {
			switch (scope.MetadataScopeType) {
			case MetadataScopeType.AssemblyNameReference:
				return convert((AssemblyNameReference)scope);

			case MetadataScopeType.ModuleDefinition:
				var mod = (ModuleDefinition)scope;
				if (mod.Assembly != null)
					return convert((AssemblyNameReference)mod.Assembly.Name);
				return convert((ModuleReference)scope);

			case MetadataScopeType.ModuleReference:
				return convert((ModuleReference)scope);

			default:
				throw new ApplicationException("Unknown MetadataScopeType");
			}
		}

		public AssemblyNameReference convert(AssemblyNameReference asmRef) {
			return DotNetUtils.addAssemblyReference(module, asmRef);
		}

		public ModuleReference convert(ModuleReference modRef) {
			return DotNetUtils.addModuleReference(module, modRef);
		}

		public TypeReference tryGetTypeDefinition(TypeReference typeRef) {
			return DotNetUtils.getType(module, typeRef) ?? typeRef;
		}

		public FieldReference tryGetFieldDefinition(FieldReference fieldRef) {
			var fieldDef = fieldRef as FieldDefinition;
			if (fieldDef != null)
				return fieldDef;

			var declaringType = DotNetUtils.getType(module, fieldRef.DeclaringType);
			if (declaringType == null)
				return fieldRef;
			return DotNetUtils.getField(declaringType, fieldRef);
		}

		public MethodReference tryGetMethodDefinition(MethodReference methodRef) {
			var methodDef = methodRef as MethodDefinition;
			if (methodDef != null)
				return methodDef;

			var declaringType = DotNetUtils.getType(module, methodRef.DeclaringType);
			if (declaringType == null)
				return methodRef;
			return DotNetUtils.getMethod(declaringType, methodRef);
		}
	}
}
