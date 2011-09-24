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
using de4dot.blocks;

namespace de4dot.renamer {
	interface INameCreator {
		INameCreator clone();
		string newName();
	}

	class OneNameCreator : INameCreator {
		string name;

		public OneNameCreator(string name) {
			this.name = name;
		}

		public INameCreator clone() {
			return this;
		}

		public string newName() {
			return name;
		}
	}

	class GlobalNameCreator : INameCreator {
		INameCreator other;

		public GlobalNameCreator(INameCreator other) {
			this.other = other;
		}

		public INameCreator clone() {
			return this;
		}

		public string newName() {
			return other.newName();
		}
	}

	class GenericParamNameCreator : INameCreator {
		static string[] names = new string[] { "T", "U", "V", "W", "X", "Y", "Z" };
		int index = 0;

		public string newName() {
			if (index < names.Length)
				return names[index++];
			return string.Format("T{0}", index++);
		}

		public INameCreator clone() {
			var rv = new GenericParamNameCreator();
			rv.index = index;
			return rv;
		}
	}

	class NameCreator : INameCreator {
		string prefix;
		int num;

		public NameCreator(string prefix, int num = 0) {
			this.prefix = prefix;
			this.num = num;
		}

		public INameCreator clone() {
			return new NameCreator(prefix, num);
		}

		public string newName() {
			return prefix + num++;
		}
	}

	// Like NameCreator but don't add the counter the first time
	class NameCreator2 : INameCreator {
		string prefix;
		int num;
		const string separator = "_";

		public NameCreator2(string prefix, int num = 0) {
			this.prefix = prefix;
			this.num = num;
		}

		public INameCreator clone() {
			return new NameCreator2(prefix, num);
		}

		public string newName() {
			string rv;
			if (num == 0)
				rv = prefix;
			else
				rv = prefix + separator + num;
			num++;
			return rv;
		}
	}

	interface ITypeNameCreator {
		string newName(TypeDefinition typeDefinition, string newBaseTypeName = null);
	}

	class PinvokeNameCreator {
		Dictionary<string, NameCreator2> nameCreators = new Dictionary<string, NameCreator2>(StringComparer.Ordinal);

		public string newName(string name) {
			NameCreator2 nameCreator;
			if (!nameCreators.TryGetValue(name, out nameCreator))
				nameCreators[name] = nameCreator = new NameCreator2(name);
			return nameCreator.newName();
		}
	}

	class NameInfos {
		IList<NameInfo> nameInfos = new List<NameInfo>();

		class NameInfo {
			public string name;
			public INameCreator nameCreator;
			public NameInfo(string name, INameCreator nameCreator) {
				this.name = name;
				this.nameCreator = nameCreator;
			}
		}

		public void add(string name, INameCreator nameCreator) {
			nameInfos.Add(new NameInfo(name, nameCreator));
		}

		public INameCreator find(string typeName) {
			foreach (var nameInfo in nameInfos) {
				if (typeName.Contains(nameInfo.name))
					return nameInfo.nameCreator;
			}

			return null;
		}
	}

	class TypeNameCreator : ITypeNameCreator {
		INameCreator createUnknownTypeName;
		INameCreator createEnumName;
		INameCreator createStructName;
		INameCreator createDelegateName;
		INameCreator createClassName;
		INameCreator createInterfaceName;
		NameInfos nameInfos = new NameInfos();

		public TypeNameCreator() {
			createUnknownTypeName = createNameCreator("Type");
			createEnumName = createNameCreator("Enum");
			createStructName = createNameCreator("Struct");
			createDelegateName = createNameCreator("Delegate");
			createClassName = createNameCreator("Class");
			createInterfaceName = createNameCreator("Interface");

			var names = new string[] {
				"Exception",
				"EventArgs",
				"Attribute",
				"Form",
				"Dialog",
				"Control",
			};
			foreach (var name in names)
				nameInfos.add(name, createNameCreator(name));
		}

		protected virtual INameCreator createNameCreator(string prefix) {
			return new NameCreator(prefix);
		}

		public string newName(TypeDefinition typeDefinition, string newBaseTypeName = null) {
			var nameCreator = getNameCreator(typeDefinition, newBaseTypeName);
			return nameCreator.newName();
		}

		INameCreator getNameCreator(TypeDefinition typeDefinition, string newBaseTypeName) {
			var nameCreator = createUnknownTypeName;
			if (typeDefinition.IsEnum)
				nameCreator = createEnumName;
			else if (typeDefinition.IsValueType)
				nameCreator = createStructName;
			else if (typeDefinition.IsClass) {
				if (typeDefinition.BaseType != null) {
					if (MemberReferenceHelper.verifyType(typeDefinition.BaseType, "mscorlib", "System.Delegate"))
						nameCreator = createDelegateName;
					else if (MemberReferenceHelper.verifyType(typeDefinition.BaseType, "mscorlib", "System.MulticastDelegate"))
						nameCreator = createDelegateName;
					else {
						nameCreator = nameInfos.find(newBaseTypeName ?? typeDefinition.BaseType.Name);
						if (nameCreator == null)
							nameCreator = createClassName;
					}
				}
				else
					nameCreator = createClassName;
			}
			else if (typeDefinition.IsInterface)
				nameCreator = createInterfaceName;
			return nameCreator;
		}
	}

	class GlobalTypeNameCreator : TypeNameCreator {
		protected override INameCreator createNameCreator(string prefix) {
			return new GlobalNameCreator(base.createNameCreator("G" + prefix));
		}
	}
}
