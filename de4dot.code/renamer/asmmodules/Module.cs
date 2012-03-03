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
using System.Collections.Generic;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.renamer.asmmodules {
	class Module : IResolver {
		IObfuscatedFile obfuscatedFile;
		TypeDefDict types = new TypeDefDict();
		MemberRefFinder memberRefFinder;
		IList<RefToDef<TypeReference, TypeDefinition>> typeRefsToRename = new List<RefToDef<TypeReference, TypeDefinition>>();
		IList<RefToDef<MethodReference, MethodDefinition>> methodRefsToRename = new List<RefToDef<MethodReference, MethodDefinition>>();
		IList<RefToDef<FieldReference, FieldDefinition>> fieldRefsToRename = new List<RefToDef<FieldReference, FieldDefinition>>();
		List<CustomAttributeReference> customAttributeFieldReferences = new List<CustomAttributeReference>();
		List<CustomAttributeReference> customAttributePropertyReferences = new List<CustomAttributeReference>();
		List<MethodDefinition> allMethods;

		public class CustomAttributeReference {
			public CustomAttribute cattr;
			public int index;
			public MemberReference reference;
			public CustomAttributeReference(CustomAttribute cattr, int index, MemberReference reference) {
				this.cattr = cattr;
				this.index = index;
				this.reference = reference;
			}
		}

		public class RefToDef<R, D> where R : MemberReference where D : R {
			public R reference;
			public D definition;
			public RefToDef(R reference, D definition) {
				this.reference = reference;
				this.definition = definition;
			}
		}

		public IEnumerable<RefToDef<TypeReference, TypeDefinition>> TypeRefsToRename {
			get { return typeRefsToRename; }
		}

		public IEnumerable<RefToDef<MethodReference, MethodDefinition>> MethodRefsToRename {
			get { return methodRefsToRename; }
		}

		public IEnumerable<RefToDef<FieldReference, FieldDefinition>> FieldRefsToRename {
			get { return fieldRefsToRename; }
		}

		public IEnumerable<CustomAttributeReference> CustomAttributeFieldReferences {
			get { return customAttributeFieldReferences; }
		}

		public IEnumerable<CustomAttributeReference> CustomAttributePropertyReferences {
			get { return customAttributePropertyReferences; }
		}

		public IObfuscatedFile ObfuscatedFile {
			get { return obfuscatedFile; }
		}

		public string Filename {
			get { return obfuscatedFile.Filename; }
		}

		public ModuleDefinition ModuleDefinition {
			get { return obfuscatedFile.ModuleDefinition; }
		}

		public Module(IObfuscatedFile obfuscatedFile) {
			this.obfuscatedFile = obfuscatedFile;
		}

		public IEnumerable<TypeDef> getAllTypes() {
			return types.getValues();
		}

		public IEnumerable<MethodDefinition> getAllMethods() {
			return allMethods;
		}

		public void findAllMemberReferences(ref int typeIndex) {
			memberRefFinder = new MemberRefFinder();
			memberRefFinder.findAll(ModuleDefinition, ModuleDefinition.Types);
			allMethods = new List<MethodDefinition>(memberRefFinder.methodDefinitions.Keys);

			var allTypesList = new List<TypeDef>();
			foreach (var type in memberRefFinder.typeDefinitions.Keys) {
				var typeDef = new TypeDef(type, this, typeIndex++);
				types.add(typeDef);
				allTypesList.Add(typeDef);
				typeDef.addMembers();
			}

			var allTypesCopy = new List<TypeDef>(allTypesList);
			var typeToIndex = new Dictionary<TypeDefinition, int>();
			for (int i = 0; i < allTypesList.Count; i++)
				typeToIndex[allTypesList[i].TypeDefinition] = i;
			foreach (var typeDef in allTypesList) {
				if (typeDef.TypeDefinition.NestedTypes == null)
					continue;
				foreach (var nestedTypeDefinition in typeDef.TypeDefinition.NestedTypes) {
					int index = typeToIndex[nestedTypeDefinition];
					var nestedTypeDef = allTypesCopy[index];
					allTypesCopy[index] = null;
					if (nestedTypeDef == null)	// Impossible
						throw new ApplicationException("Nested type belongs to two or more types");
					typeDef.add(nestedTypeDef);
					nestedTypeDef.NestingType = typeDef;
				}
			}
		}

		public void resolveAllRefs(IResolver resolver) {
			foreach (var typeRef in memberRefFinder.typeReferences.Keys) {
				var typeDef = resolver.resolve(typeRef);
				if (typeDef != null)
					typeRefsToRename.Add(new RefToDef<TypeReference, TypeDefinition>(typeRef, typeDef.TypeDefinition));
			}

			foreach (var methodRef in memberRefFinder.methodReferences.Keys) {
				var methodDef = resolver.resolve(methodRef);
				if (methodDef != null)
					methodRefsToRename.Add(new RefToDef<MethodReference, MethodDefinition>(methodRef, methodDef.MethodDefinition));
			}

			foreach (var fieldRef in memberRefFinder.fieldReferences.Keys) {
				var fieldDef = resolver.resolve(fieldRef);
				if (fieldDef != null)
					fieldRefsToRename.Add(new RefToDef<FieldReference, FieldDefinition>(fieldRef, fieldDef.FieldDefinition));
			}

			foreach (var cattr in memberRefFinder.customAttributes.Keys) {
				try {
					var typeDef = resolver.resolve(cattr.AttributeType);
					if (typeDef == null)
						continue;

					for (int i = 0; i < cattr.Fields.Count; i++) {
						var field = cattr.Fields[i];
						var fieldDef = findFieldByName(typeDef, field.Name);
						if (fieldDef == null) {
							Log.w("Could not find field {0} in attribute {1} ({2:X8})",
									Utils.toCsharpString(field.Name),
									Utils.toCsharpString(typeDef.TypeDefinition.Name),
									typeDef.TypeDefinition.MetadataToken.ToInt32());
							continue;
						}

						customAttributeFieldReferences.Add(new CustomAttributeReference(cattr, i, fieldDef.FieldDefinition));
					}

					for (int i = 0; i < cattr.Properties.Count; i++) {
						var prop = cattr.Properties[i];
						var propDef = findPropertyByName(typeDef, prop.Name);
						if (propDef == null) {
							Log.w("Could not find property {0} in attribute {1} ({2:X8})",
									Utils.toCsharpString(prop.Name),
									Utils.toCsharpString(typeDef.TypeDefinition.Name),
									typeDef.TypeDefinition.MetadataToken.ToInt32());
							continue;
						}

						customAttributePropertyReferences.Add(new CustomAttributeReference(cattr, i, propDef.PropertyDefinition));
					}
				}
				catch {
				}
			}
		}

		static FieldDef findFieldByName(TypeDef typeDef, string name) {
			while (typeDef != null) {
				foreach (var fieldDef in typeDef.AllFields) {
					if (fieldDef.FieldDefinition.Name == name)
						return fieldDef;
				}

				if (typeDef.baseType == null)
					break;
				typeDef = typeDef.baseType.typeDef;
			}
			return null;
		}

		static PropertyDef findPropertyByName(TypeDef typeDef, string name) {
			while (typeDef != null) {
				foreach (var propDef in typeDef.AllProperties) {
					if (propDef.PropertyDefinition.Name == name)
						return propDef;
				}

				if (typeDef.baseType == null)
					break;
				typeDef = typeDef.baseType.typeDef;
			}
			return null;
		}

		public void onTypesRenamed() {
			var newTypes = new TypeDefDict();
			foreach (var typeDef in types.getValues()) {
				typeDef.onTypesRenamed();
				newTypes.add(typeDef);
			}
			types = newTypes;
		}

		static TypeReference getNonGenericTypeReference(TypeReference typeReference) {
			if (typeReference == null)
				return null;
			if (!typeReference.IsGenericInstance)
				return typeReference;
			var type = (GenericInstanceType)typeReference;
			return type.ElementType;
		}

		public TypeDef resolve(TypeReference typeReference) {
			return this.types.find(getNonGenericTypeReference(typeReference));
		}

		public MethodDef resolve(MethodReference methodReference) {
			var typeDef = this.types.find(getNonGenericTypeReference(methodReference.DeclaringType));
			if (typeDef == null)
				return null;
			return typeDef.find(methodReference);
		}

		public FieldDef resolve(FieldReference fieldReference) {
			var typeDef = this.types.find(getNonGenericTypeReference(fieldReference.DeclaringType));
			if (typeDef == null)
				return null;
			return typeDef.find(fieldReference);
		}
	}
}
