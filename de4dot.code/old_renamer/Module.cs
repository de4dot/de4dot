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

namespace de4dot.old_renamer {
	class Module : IResolver {
		IObfuscatedFile obfuscatedFile;
		MemberRefFinder memberRefFinder;
		TypeDefDict allTypes = new TypeDefDict();
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

		public void onBeforeRenamingTypeDefinitions() {
			if (obfuscatedFile.RemoveNamespaceWithOneType)
				removeOneClassNamespaces();
		}

		void removeOneClassNamespaces() {
			var nsToTypes = new Dictionary<string, List<TypeDef>>(StringComparer.Ordinal);

			foreach (var typeDef in allTypes.getAll()) {
				List<TypeDef> list;
				var ns = typeDef.TypeDefinition.Namespace;
				if (string.IsNullOrEmpty(ns))
					continue;
				if (IsValidName(ns))
					continue;
				if (!nsToTypes.TryGetValue(ns, out list))
					nsToTypes[ns] = list = new List<TypeDef>();
				list.Add(typeDef);
			}

			var sortedNamespaces = new List<List<TypeDef>>(nsToTypes.Values);
			sortedNamespaces.Sort((a, b) => {
				return string.CompareOrdinal(a[0].TypeDefinition.Namespace, b[0].TypeDefinition.Namespace);
			});
			foreach (var list in sortedNamespaces) {
				const int maxClasses = 1;
				if (list.Count != maxClasses)
					continue;
				var ns = list[0].TypeDefinition.Namespace;
				Log.v("Removing namespace: {0}", ns);
				foreach (var type in list)
					type.NewNamespace = "";
			}
		}

		static string renameResourceString(string s, string oldTypeName, string newTypeName) {
			if (!Utils.StartsWith(s, oldTypeName, StringComparison.Ordinal))
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

			// Rename the longest names first. Otherwise eg. b.g.resources could be renamed
			// Class0.g.resources instead of Class1.resources when b.g was renamed Class1.
			renamedTypes.Sort((a, b) => {
				if (a.OldName.Length > b.OldName.Length) return -1;
				if (a.OldName.Length < b.OldName.Length) return 1;
				return 0;
			});

			renameResourceNamesInCode(renamedTypes);
			renameResources(renamedTypes);
		}

		void renameResourceNamesInCode(IEnumerable<Renamed> renamedTypes) {
			// This is needed to speed up this method
			var oldToNewTypeName = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var renamed in renamedTypes)
				oldToNewTypeName[renamed.OldName] = renamed.NewName;

			List<string> validResourceNames = new List<string>();
			if (ModuleDefinition.Resources != null) {
				foreach (var resource in ModuleDefinition.Resources) {
					var name = resource.Name;
					if (name.EndsWith(".resources", StringComparison.Ordinal))
						validResourceNames.Add(name);
				}
			}

			foreach (var method in allMethods) {
				if (!method.HasBody)
					continue;
				foreach (var instr in method.Body.Instructions) {
					if (instr.OpCode != OpCodes.Ldstr)
						continue;
					var s = (string)instr.Operand;
					if (string.IsNullOrEmpty(s))
						continue;	// Ignore emtpy strings since we'll get lots of false warnings

					string newName = null;
					string oldName = null;
					if (oldToNewTypeName.ContainsKey(s)) {
						oldName = s;
						newName = oldToNewTypeName[s];
					}
					else if (s.EndsWith(".resources", StringComparison.Ordinal)) {
						// This should rarely, if ever, execute...
						foreach (var renamed in renamedTypes) {	// Slow loop
							var newName2 = renameResourceString(s, renamed.OldName, renamed.NewName);
							if (newName2 != s) {
								newName = newName2;
								oldName = renamed.OldName;
								break;
							}
						}
					}
					if (newName == null || string.IsNullOrEmpty(oldName))
						continue;

					bool isValid = false;
					foreach (var validName in validResourceNames) {
						if (Utils.StartsWith(validName, oldName, StringComparison.Ordinal)) {
							isValid = true;
							break;
						}
					}
					if (!isValid)
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

		public void findAllMemberReferences(ref int typeIndex) {
			memberRefFinder = new MemberRefFinder();
			memberRefFinder.findAll(ModuleDefinition, ModuleDefinition.Types);
			allMethods = new List<MethodDefinition>(memberRefFinder.methodDefinitions.Keys);

			var allTypesList = new List<TypeDef>();
			foreach (var type in new List<TypeDefinition>(memberRefFinder.typeDefinitions.Keys)) {
				memberRefFinder.removeTypeDefinition(type);
				var typeDef = new TypeDef(type, this, typeIndex++);
				allTypes.add(typeDef);
				allTypesList.Add(typeDef);

				typeDef.addMembers();

				foreach (var ev in type.Events)
					memberRefFinder.removeEventDefinition(ev);
				foreach (var field in type.Fields)
					memberRefFinder.removeFieldDefinition(field);
				foreach (var method in type.Methods)
					memberRefFinder.removeMethodDefinition(method);
				foreach (var property in type.Properties)
					memberRefFinder.removePropertyDefinition(property);
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
			var newAllTypes = new TypeDefDict();
			foreach (var typeDef in allTypes.getAll()) {
				typeDef.onTypesRenamed();
				newAllTypes.add(typeDef);
			}
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

		public TypeDef resolve(TypeReference typeReference) {
			return this.allTypes.find(getNonGenericTypeReference(typeReference));
		}

		public MethodDef resolve(MethodReference methodReference) {
			var typeDef = this.allTypes.find(getNonGenericTypeReference(methodReference.DeclaringType));
			if (typeDef == null)
				return null;
			return typeDef.find(methodReference);
		}

		public FieldDef resolve(FieldReference fieldReference) {
			var typeDef = this.allTypes.find(getNonGenericTypeReference(fieldReference.DeclaringType));
			if (typeDef == null)
				return null;
			return typeDef.find(fieldReference);
		}
	}
}
