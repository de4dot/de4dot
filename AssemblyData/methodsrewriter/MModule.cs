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
using System.Collections.Generic;
using System.Reflection;
using dnlib.DotNet;
using de4dot.blocks;

namespace AssemblyData.methodsrewriter {
	class MModule {
		public Module module;
		public ModuleDefMD moduleDef;
		TypeDefDict<MType> typeRefToType = new TypeDefDict<MType>();
		Dictionary<int, MType> tokenToType = new Dictionary<int, MType>();
		Dictionary<int, MMethod> tokenToGlobalMethod;
		Dictionary<int, MField> tokenToGlobalField;
		TypeDef moduleType;

		public MModule(Module module, ModuleDefMD moduleDef) {
			this.module = module;
			this.moduleDef = moduleDef;
			initTokenToType();
		}

		void initTokenToType() {
			moduleType = moduleDef.Types[0];
			foreach (var typeDef in moduleDef.GetTypes()) {
				int token = (int)typeDef.MDToken.Raw;
				Type type;
				try {
					type = module.ResolveType(token);
				}
				catch {
					tokenToType[token] = null;
					typeRefToType.add(typeDef, null);
					continue;
				}
				var mtype = new MType(type, typeDef);
				tokenToType[token] = mtype;
				typeRefToType.add(typeDef, mtype);
			}
		}

		public MType getType(IType typeRef) {
			return typeRefToType.find(typeRef);
		}

		public MMethod getMethod(IMethod methodRef) {
			var type = getType(methodRef.DeclaringType);
			if (type != null)
				return type.getMethod(methodRef);
			if (!new SigComparer().Equals(moduleType, methodRef.DeclaringType))
				return null;

			initGlobalMethods();
			foreach (var method in tokenToGlobalMethod.Values) {
				if (new SigComparer().Equals(methodRef, method.methodDef))
					return method;
			}

			return null;
		}

		public MField getField(IField fieldRef) {
			var type = getType(fieldRef.DeclaringType);
			if (type != null)
				return type.getField(fieldRef);
			if (!new SigComparer().Equals(moduleType, fieldRef.DeclaringType))
				return null;

			initGlobalFields();
			foreach (var field in tokenToGlobalField.Values) {
				if (new SigComparer().Equals(fieldRef, field.fieldDef))
					return field;
			}

			return null;
		}

		public MMethod getMethod(MethodBase method) {
			if (method.Module != module)
				throw new ApplicationException("Not our module");
			if (method.DeclaringType == null)
				return getGlobalMethod(method);
			var type = tokenToType[method.DeclaringType.MetadataToken];
			return type.getMethod(method.MetadataToken);
		}

		public MMethod getGlobalMethod(MethodBase method) {
			initGlobalMethods();
			return tokenToGlobalMethod[method.MetadataToken];
		}

		void initGlobalMethods() {
			if (tokenToGlobalMethod != null)
				return;
			tokenToGlobalMethod = new Dictionary<int, MMethod>();

			var tmpTokenToGlobalMethod = new Dictionary<int, MethodInfo>();
			var flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
			foreach (var m in module.GetMethods(flags))
				tmpTokenToGlobalMethod[m.MetadataToken] = m;
			foreach (var m in moduleType.Methods) {
				if (m.Name == ".cctor")	//TODO: Use module.GetMethod(token) to get .cctor method
					continue;
				var token = (int)m.MDToken.Raw;
				tokenToGlobalMethod[token] = new MMethod(tmpTokenToGlobalMethod[token], m);
			}
		}

		void initGlobalFields() {
			if (tokenToGlobalField != null)
				return;
			tokenToGlobalField = new Dictionary<int, MField>();

			var tmpTokenToGlobalField = new Dictionary<int, FieldInfo>();
			var flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
			foreach (var f in module.GetFields(flags))
				tmpTokenToGlobalField[f.MetadataToken] = f;
			foreach (var f in moduleType.Fields) {
				var token = (int)f.MDToken.Raw;
				tokenToGlobalField[token] = new MField(tmpTokenToGlobalField[token], f);
			}
		}

		public override string ToString() {
			return moduleDef.Location;
		}
	}
}
