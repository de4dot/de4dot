/*
    Copyright (C) 2011 de4dot@gmail.com

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
using Mono.Cecil.Cil;
using de4dot.deobfuscators;

namespace de4dot.renamer {
	class Module : IResolver {
		IObfuscatedFile obfuscatedFile;
		MemberRefFinder memberRefFinder;
		DefDict<TypeDef> allTypes = new DefDict<TypeDef>();
		IList<RefToDef<TypeReference, TypeDefinition>> typeRefsToRename = new List<RefToDef<TypeReference, TypeDefinition>>();
		IList<RefToDef<MethodReference, MethodDefinition>> methodRefsToRename = new List<RefToDef<MethodReference, MethodDefinition>>();
		IList<RefToDef<FieldReference, FieldDefinition>> fieldRefsToRename = new List<RefToDef<FieldReference, FieldDefinition>>();
		List<MethodDefinition> allMethods;

		public Func<string, bool> IsValidName {
			get { return obfuscatedFile.IsValidName; }
		}

		class RefToDef<R, D> where R : MemberReference where D : R {
			public R reference;
			public D definition;
			public RefToDef(R reference, D definition) {
				this.reference = reference;
				this.definition = definition;
			}
		}

		public string Filename {
			get { return obfuscatedFile.Filename; }
		}

		public ModuleDefinition ModuleDefinition {
			get { return obfuscatedFile.ModuleDefinition; }
		}

		public string Pathname {
			get { return ModuleDefinition.FullyQualifiedName; }
		}

		public Module(IObfuscatedFile obfuscatedFile) {
			this.obfuscatedFile = obfuscatedFile;
		}

		public IEnumerable<TypeDef> getAllTypes() {
			return allTypes.getAll();
		}

		IEnumerable<Renamed> getRenamedTypeNames() {
			foreach (var typeDef in allTypes.getAll()) {
				if (typeDef.OldFullName != typeDef.TypeDefinition.FullName) {
					yield return new Renamed {
						OldName = typeDef.OldFullName,
						NewName = typeDef.TypeDefinition.FullName
					};
				}
			}
		}

		static string renameResourceString(string s, string oldTypeName, string newTypeName) {
			if (!s.StartsWith(oldTypeName, StringComparison.Ordinal))
				return s;
			if (s.Length == oldTypeName.Length)
				return newTypeName;
			// s.Length > oldTypeName.Length
			if (s[oldTypeName.Length] != '.')
				return s;
			if (!s.EndsWith(".resources", StringComparison.Ordinal))
				return s;
			return newTypeName + s.Substring(oldTypeName.Length);
		}

		public void renameResources() {
			var renamedTypes = new List<Renamed>(getRenamedTypeNames());
			renameResourceNamesInCode(renamedTypes);
			renameResources(renamedTypes);
		}

		void renameResourceNamesInCode(IEnumerable<Renamed> renamedTypes) {
			// This is needed to speed up this method
			var oldToNewTypeName = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var renamed in renamedTypes)
				oldToNewTypeName[renamed.OldName] = renamed.NewName;

			foreach (var method in allMethods) {
				if (!method.HasBody)
					continue;
				foreach (var instr in method.Body.Instructions) {
					if (instr.OpCode != OpCodes.Ldstr)
						continue;
					var s = (string)instr.Operand;

					string newName = null;
					if (oldToNewTypeName.ContainsKey(s))
						newName = oldToNewTypeName[s];
					else if (s.EndsWith(".resources", StringComparison.Ordinal)) {
						// This should rarely, if ever, execute...
						foreach (var renamed in renamedTypes) {	// Slow loop
							var newName2 = renameResourceString(s, renamed.OldName, renamed.NewName);
							if (newName2 != s) {
								newName = newName2;
								break;
							}
						}
					}
					if (newName == null)
						continue;

					if (s == "" || !obfuscatedFile.RenameResourcesInCode)
						Log.v("Possible resource name in code: '{0}' => '{1}' in method {2}", s, newName, method);
					else {
						instr.Operand = newName;
						Log.v("Renamed resource string in code: '{0}' => '{1}' ({2})", s, newName, method);
						break;
					}
				}
			}
		}

		void renameResources(IEnumerable<Renamed> renamedTypes) {
			if (ModuleDefinition.Resources == null)
				return;
			foreach (var resource in ModuleDefinition.Resources) {
				var s = resource.Name;
				foreach (var renamed in renamedTypes) {
					var newName = renameResourceString(s, renamed.OldName, renamed.NewName);
					if (newName != s) {
						resource.Name = newName;
						Log.v("Renamed resource in resources: {0} => {1}", s, newName);
						break;
					}
				}
			}
		}

		public void findAllMemberReferences() {
			memberRefFinder = new MemberRefFinder();
			memberRefFinder.findAll(ModuleDefinition, ModuleDefinition.Types);
			allMethods = new List<MethodDefinition>(memberRefFinder.methodDefinitions.Keys);

			var allTypesList = new List<TypeDef>();
			foreach (var type in new List<TypeDefinition>(memberRefFinder.typeDefinitions.Keys)) {
				memberRefFinder.removeTypeDefinition(type);
				var typeDef = new TypeDef(type, this);
				allTypes.add(typeDef);
				allTypesList.Add(typeDef);

				if (type.Events != null) {
					for (int i = 0; i < type.Events.Count; i++) {
						var ev = type.Events[i];
						typeDef.add(new EventDef(ev, typeDef, i));
						memberRefFinder.removeEventDefinition(ev);
					}
				}
				if (type.Fields != null) {
					for (int i = 0; i < type.Fields.Count; i++) {
						var field = type.Fields[i];
						typeDef.add(new FieldDef(field, typeDef, i));
						memberRefFinder.removeFieldDefinition(field);
					}
				}
				if (type.Methods != null) {
					for (int i = 0; i < type.Methods.Count; i++) {
						var method = type.Methods[i];
						typeDef.add(new MethodDef(method, typeDef, i));
						memberRefFinder.removeMethodDefinition(method);
					}
				}
				if (type.Properties != null) {
					for (int i = 0; i < type.Properties.Count; i++) {
						var property = type.Properties[i];
						typeDef.add(new PropertyDef(property, typeDef, i));
						memberRefFinder.removePropertyDefinition(property);
					}
				}

				typeDef.membersAdded();
			}

			// Add all nested types to the correct TypeDef's types list
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

			// Make sure we got all definitions
			if (memberRefFinder.typeDefinitions.Count > 0)
				throw new ApplicationException("There are types left");
			if (memberRefFinder.eventDefinitions.Count > 0)
				throw new ApplicationException("There are events left");
			if (memberRefFinder.fieldDefinitions.Count > 0)
				throw new ApplicationException("There are fields left");
			if (memberRefFinder.methodDefinitions.Count > 0)
				throw new ApplicationException("There are methods left");
			if (memberRefFinder.propertyDefinitions.Count > 0)
				throw new ApplicationException("There are properties left");
		}

		public void resolveAllRefs(IResolver resolver) {
			foreach (var typeRef in memberRefFinder.typeReferences.Keys) {
				var typeDefinition = resolver.resolve(typeRef);
				if (typeDefinition != null)
					typeRefsToRename.Add(new RefToDef<TypeReference, TypeDefinition>(typeRef, typeDefinition));
			}

			foreach (var methodRef in memberRefFinder.methodReferences.Keys) {
				var methodDefinition = resolver.resolve(methodRef);
				if (methodDefinition != null)
					methodRefsToRename.Add(new RefToDef<MethodReference, MethodDefinition>(methodRef, methodDefinition));
			}

			foreach (var fieldRef in memberRefFinder.fieldReferences.Keys) {
				var fieldDefinition = resolver.resolve(fieldRef);
				if (fieldDefinition != null)
					fieldRefsToRename.Add(new RefToDef<FieldReference, FieldDefinition>(fieldRef, fieldDefinition));
			}
		}

		public void renameTypeReferences() {
			foreach (var refToDef in typeRefsToRename) {
				refToDef.reference.Name = refToDef.definition.Name;
				refToDef.reference.Namespace = refToDef.definition.Namespace;
			}
		}

		public void renameMemberReferences() {
			foreach (var refToDef in methodRefsToRename)
				refToDef.reference.Name = refToDef.definition.Name;
			foreach (var refToDef in fieldRefsToRename)
				refToDef.reference.Name = refToDef.definition.Name;
		}

		public void onTypesRenamed() {
			rebuildAllTypesDict();
		}

		void rebuildAllTypesDict() {
			var newAllTypes = new DefDict<TypeDef>();
			foreach (var typeDef in allTypes.getAll())
				newAllTypes.add(typeDef);
			allTypes = newAllTypes;
		}

		static TypeReference getNonGenericTypeReference(TypeReference typeReference) {
			if (typeReference == null)
				return null;
			if (!typeReference.IsGenericInstance)
				return typeReference;
			var type = (GenericInstanceType)typeReference;
			return type.ElementType;
		}

		public TypeDefinition resolve(TypeReference typeReference) {
			var typeDef = this.allTypes.find(getNonGenericTypeReference(typeReference));
			if (typeDef == null)
				return null;
			return typeDef.TypeDefinition;
		}

		public MethodDefinition resolve(MethodReference methodReference) {
			var typeDef = this.allTypes.find(getNonGenericTypeReference(methodReference.DeclaringType));
			if (typeDef == null)
				return null;
			var methodDef = typeDef.find(methodReference);
			if (methodDef == null)
				return null;
			return methodDef.MethodDefinition;
		}

		public FieldDefinition resolve(FieldReference fieldReference) {
			var typeDef = this.allTypes.find(getNonGenericTypeReference(fieldReference.DeclaringType));
			if (typeDef == null)
				return null;
			var fieldDef = typeDef.find(fieldReference);
			if (fieldDef == null)
				return null;
			return fieldDef.FieldDefinition;
		}
	}
}
