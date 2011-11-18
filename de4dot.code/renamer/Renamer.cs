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

using System.Collections.Generic;
using de4dot.renamer.asmmodules;

namespace de4dot.renamer {
	class Renamer {
		public bool RenameNamespaces { get; set; }
		public bool RenameTypes { get; set; }
		public bool RenameGenericParams { get; set; }
		public bool RenameProperties { get; set; }
		public bool RenameEvents { get; set; }
		public bool RenameFields { get; set; }
		public bool RenameMethods { get; set; }
		public bool RenameMethodArgs { get; set; }
		Modules modules = new Modules();
		MemberInfos memberInfos = new MemberInfos();

		public Renamer(IEnumerable<IObfuscatedFile> files) {
			RenameNamespaces = true;
			RenameTypes = true;
			RenameProperties = true;
			RenameEvents = true;
			RenameFields = true;
			RenameGenericParams = true;
			RenameMethodArgs = true;

			foreach (var file in files)
				modules.add(new Module(file));
		}

		public void rename() {
			if (modules.Empty)
				return;
			Log.n("Renaming all obfuscated symbols");

			modules.initialize();
			modules.initializeVirtualMembers();
			memberInfos.initialize(modules);
			renameTypeDefinitions();
			renameTypeReferences();
			modules.onTypesRenamed();
			prepareRenameMemberDefinitions();
			modules.cleanUp();
		}

		void renameTypeDefinitions() {
			Log.v("Renaming obfuscated type definitions");

			prepareRenameTypes(modules.BaseTypes, new TypeRenamerState());
			fixClsTypeNames();
			renameTypeDefinitions(modules.NonNestedTypes);
		}

		void renameTypeDefinitions(IEnumerable<TypeDef> typeDefs) {
			Log.indent();
			foreach (var typeDef in typeDefs) {
				rename(typeDef);
				renameTypeDefinitions(typeDef.NestedTypes);
			}
			Log.deIndent();
		}

		void rename(TypeDef type) {
			var typeDefinition = type.TypeDefinition;
			var info = memberInfos.type(type);

			Log.v("Type: {0} ({1:X8})", typeDefinition.FullName, typeDefinition.MetadataToken.ToUInt32());
			Log.indent();

			renameGenericParams(type.GenericParams);

			if (RenameTypes && info.gotNewName()) {
				var old = typeDefinition.Name;
				typeDefinition.Name = info.newName;
				Log.v("Name: {0} => {1}", old, typeDefinition.Name);
			}

			if (RenameNamespaces && info.newNamespace != null) {
				var old = typeDefinition.Namespace;
				typeDefinition.Namespace = info.newNamespace;
				Log.v("Namespace: {0} => {1}", old, typeDefinition.Namespace);
			}

			Log.deIndent();
		}

		void renameGenericParams(IEnumerable<GenericParamDef> genericParams) {
			if (!RenameGenericParams)
				return;
			foreach (var param in genericParams) {
				var info = memberInfos.gparam(param);
				if (!info.gotNewName())
					continue;
				param.GenericParameter.Name = info.newName;
				Log.v("GenParam: {0} => {1}", info.oldFullName, param.GenericParameter.FullName);
			}
		}

		// Make sure the renamed types are using valid CLS names. That means renaming all
		// generic types from eg. Class1 to Class1`2. If we don't do this, some decompilers
		// (eg. ILSpy v1.0) won't produce correct output.
		void fixClsTypeNames() {
			foreach (var type in modules.NonNestedTypes)
				fixClsTypeNames(null, type);
		}

		void fixClsTypeNames(TypeDef nesting, TypeDef nested) {
			int nestingCount = nesting == null ? 0 : nesting.GenericParams.Count;
			int arity = nested.GenericParams.Count - nestingCount;
			var nestedInfo = memberInfos.type(nested);
			if (nestedInfo.renamed && arity > 0)
				nestedInfo.newName += "`" + arity;
			foreach (var nestedType in nested.NestedTypes)
				fixClsTypeNames(nested, nestedType);
		}

		void prepareRenameTypes(IEnumerable<TypeDef> types, TypeRenamerState state) {
			foreach (var typeDef in types) {
				memberInfos.type(typeDef).prepareRenameTypes(state);
				prepareRenameTypes(typeDef.derivedTypes, state);
			}
		}

		void renameTypeReferences() {
			Log.v("Renaming references to type definitions");
			var theModules = modules.TheModules;
			foreach (var module in theModules) {
				if (theModules.Count > 1)
					Log.v("Renaming references to type definitions ({0})", module.Filename);
				Log.indent();
				foreach (var refToDef in module.TypeRefsToRename) {
					refToDef.reference.Name = refToDef.definition.Name;
					refToDef.reference.Namespace = refToDef.definition.Namespace;
				}
				Log.deIndent();
			}
		}

		void prepareRenameMemberDefinitions() {
			Log.v("Renaming member definitions #1");

			prepareRenameEntryPoints();

			foreach (var typeDef in modules.BaseTypes)
				memberInfos.type(typeDef).variableNameState = new VariableNameState();

			foreach (var typeDef in modules.AllTypes)
				prepareRenameMembers(typeDef);
		}

		Dictionary<TypeDef, bool> prepareRenameMembersCalled = new Dictionary<TypeDef, bool>();
		void prepareRenameMembers(TypeDef type) {
			if (prepareRenameMembersCalled.ContainsKey(type))
				return;
			prepareRenameMembersCalled[type] = true;

			foreach (var ifaceInfo in type.interfaces)
				prepareRenameMembers(ifaceInfo.typeDef);
			if (type.baseType != null)
				prepareRenameMembers(type.baseType.typeDef);

			TypeInfo info;
			if (memberInfos.tryGetType(type, out info))
				info.prepareRenameMembers();
		}

		void prepareRenameEntryPoints() {
			foreach (var module in modules.TheModules) {
				var entryPoint = module.ModuleDefinition.EntryPoint;
				if (entryPoint == null)
					continue;
				var methodDef = modules.resolve(entryPoint);
				if (methodDef == null) {
					Log.w(string.Format("Could not find entry point. Module: {0}, Method: {1}", module.ModuleDefinition.FullyQualifiedName, entryPoint));
					continue;
				}
				if (!methodDef.isStatic())
					continue;
				memberInfos.method(methodDef).suggestedName = "Main";
				if (methodDef.ParamDefs.Count == 1) {
					var paramDef = methodDef.ParamDefs[0];
					var type = paramDef.ParameterDefinition.ParameterType;
					if (type.FullName == "System.String[]")
						memberInfos.param(paramDef).newName = "args";
				}
			}
		}
	}
}
