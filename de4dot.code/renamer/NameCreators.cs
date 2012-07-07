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

using System.Collections.Generic;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.renamer {
	interface INameCreator {
		string create();
	}

	class OneNameCreator : INameCreator {
		string name;

		public OneNameCreator(string name) {
			this.name = name;
		}

		public string create() {
			return name;
		}
	}

	abstract class NameCreatorCounter : INameCreator {
		protected int num;

		public abstract string create();

		public NameCreatorCounter merge(NameCreatorCounter other) {
			if (num < other.num)
				num = other.num;
			return this;
		}
	}

	class GenericParamNameCreator : NameCreatorCounter {
		static string[] names = new string[] { "T", "U", "V", "W", "X", "Y", "Z" };

		public override string create() {
			if (num < names.Length)
				return names[num++];
			return string.Format("T{0}", num++);
		}
	}

	class NameCreator : NameCreatorCounter {
		string prefix;

		public NameCreator(string prefix)
			: this(prefix, 0) {
		}

		public NameCreator(string prefix, int num) {
			this.prefix = prefix;
			this.num = num;
		}

		public NameCreator clone() {
			return new NameCreator(prefix, num);
		}

		public override string create() {
			return prefix + num++;
		}
	}

	// Like NameCreator but don't add the counter the first time
	class NameCreator2 : NameCreatorCounter {
		string prefix;
		const string separator = "_";

		public NameCreator2(string prefix)
			: this(prefix, 0) {
		}

		public NameCreator2(string prefix, int num) {
			this.prefix = prefix;
			this.num = num;
		}

		public override string create() {
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
		string create(TypeDefinition typeDefinition, string newBaseTypeName);
	}

	class NameInfos {
		IList<NameInfo> nameInfos = new List<NameInfo>();

		class NameInfo {
			public string name;
			public NameCreator nameCreator;
			public NameInfo(string name, NameCreator nameCreator) {
				this.name = name;
				this.nameCreator = nameCreator;
			}
		}

		public void add(string name, NameCreator nameCreator) {
			nameInfos.Add(new NameInfo(name, nameCreator));
		}

		public NameCreator find(string typeName) {
			foreach (var nameInfo in nameInfos) {
				if (typeName.Contains(nameInfo.name))
					return nameInfo.nameCreator;
			}

			return null;
		}
	}

	class TypeNameCreator : ITypeNameCreator {
		ExistingNames existingNames;
		NameCreator createUnknownTypeName;
		NameCreator createEnumName;
		NameCreator createStructName;
		NameCreator createDelegateName;
		NameCreator createClassName;
		NameCreator createInterfaceName;
		NameInfos nameInfos = new NameInfos();

		public TypeNameCreator(ExistingNames existingNames) {
			this.existingNames = existingNames;
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
				"Stream",
			};
			foreach (var name in names)
				nameInfos.add(name, createNameCreator(name));
		}

		protected virtual NameCreator createNameCreator(string prefix) {
			return new NameCreator(prefix);
		}

		public string create(TypeDefinition typeDefinition, string newBaseTypeName) {
			var nameCreator = getNameCreator(typeDefinition, newBaseTypeName);
			return existingNames.getName(typeDefinition.Name, nameCreator);
		}

		NameCreator getNameCreator(TypeDefinition typeDefinition, string newBaseTypeName) {
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
		public GlobalTypeNameCreator(ExistingNames existingNames)
			: base(existingNames) {
		}

		protected override NameCreator createNameCreator(string prefix) {
			return base.createNameCreator("G" + prefix);
		}
	}
}
