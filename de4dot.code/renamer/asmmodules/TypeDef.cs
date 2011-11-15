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

namespace de4dot.renamer.asmmodules {
	class TypeInfo {
		public TypeReference typeReference;
		public TypeDef typeDef;
		public TypeInfo(TypeReference typeReference, TypeDef typeDef) {
			this.typeReference = typeReference;
			this.typeDef = typeDef;
		}
	}

	class TypeDef : Ref {
		EventDefDict events = new EventDefDict();
		FieldDefDict fields = new FieldDefDict();
		MethodDefDict methods = new MethodDefDict();
		PropertyDefDict properties = new PropertyDefDict();
		TypeDefDict types = new TypeDefDict();
		internal TypeInfo baseType = null;
		internal IList<TypeInfo> interfaces = new List<TypeInfo>();	// directly implemented interfaces
		internal IList<TypeDef> derivedTypes = new List<TypeDef>();
		Module module;

		public bool HasModule {
			get { return module != null; }
		}

		public IEnumerable<TypeDef> NestedTypes {
			get { return types.getSorted(); }
		}

		public TypeDef NestingType { get; set; }

		public TypeDefinition TypeDefinition {
			get { return (TypeDefinition)memberReference; }
		}

		public TypeDef(TypeDefinition typeDefinition, Module module, int index)
			: base(typeDefinition, null, index) {
			this.module = module;
		}

		public void addInterface(TypeDef ifaceDef, TypeReference iface) {
			if (ifaceDef == null || iface == null)
				return;
			interfaces.Add(new TypeInfo(iface, ifaceDef));
		}

		public void addBaseType(TypeDef baseDef, TypeReference baseRef) {
			if (baseDef == null || baseRef == null)
				return;
			baseType = new TypeInfo(baseRef, baseDef);
		}

		public void add(EventDef e) {
			events.add(e);
		}

		public void add(FieldDef f) {
			fields.add(f);
		}

		public void add(MethodDef m) {
			methods.add(m);
		}

		public void add(PropertyDef p) {
			properties.add(p);
		}

		public void add(TypeDef t) {
			types.add(t);
		}

		public MethodDef find(MethodReference mr) {
			return methods.find(mr);
		}

		public FieldDef find(FieldReference fr) {
			return fields.find(fr);
		}

		public void addMembers() {
			var type = TypeDefinition;

			for (int i = 0; i < type.Events.Count; i++)
				add(new EventDef(type.Events[i], this, i));
			for (int i = 0; i < type.Fields.Count; i++)
				add(new FieldDef(type.Fields[i], this, i));
			for (int i = 0; i < type.Methods.Count; i++)
				add(new MethodDef(type.Methods[i], this, i));
			for (int i = 0; i < type.Properties.Count; i++)
				add(new PropertyDef(type.Properties[i], this, i));
		}
	}
}
