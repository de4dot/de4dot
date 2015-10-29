/*
    Copyright (C) 2011-2015 de4dot@gmail.com

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
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.renamer {
	public interface INameCreator {
		string Create();
	}

	public class OneNameCreator : INameCreator {
		string name;

		public OneNameCreator(string name) {
			this.name = name;
		}

		public string Create() {
			return name;
		}
	}

	public abstract class NameCreatorCounter : INameCreator {
		protected int num;

		public abstract string Create();

		public NameCreatorCounter Merge(NameCreatorCounter other) {
			if (num < other.num)
				num = other.num;
			return this;
		}
	}

	public class GenericParamNameCreator : NameCreatorCounter {
		static string[] names = new string[] { "T", "U", "V", "W", "X", "Y", "Z" };

		public override string Create() {
			if (num < names.Length)
				return names[num++];
			return string.Format("T{0}", num++);
		}
	}

	public class NameCreator : NameCreatorCounter {
		string prefix;

		public NameCreator(string prefix)
			: this(prefix, 0) {
		}

		public NameCreator(string prefix, int num) {
			this.prefix = prefix;
			this.num = num;
		}

		public NameCreator Clone() {
			return new NameCreator(prefix, num);
		}

		public override string Create() {
			return prefix + num++;
		}
	}

	// Like NameCreator but don't add the counter the first time
	public class NameCreator2 : NameCreatorCounter {
		string prefix;
		const string separator = "_";

		public NameCreator2(string prefix)
			: this(prefix, 0) {
		}

		public NameCreator2(string prefix, int num) {
			this.prefix = prefix;
			this.num = num;
		}

		public override string Create() {
			string rv;
			if (num == 0)
				rv = prefix;
			else
				rv = prefix + separator + num;
			num++;
			return rv;
		}
	}

	public interface ITypeNameCreator {
		string Create(TypeDef typeDef, string newBaseTypeName);
	}

	public class NameInfos {
		IList<NameInfo> nameInfos = new List<NameInfo>();

		class NameInfo {
			public string name;
			public NameCreator nameCreator;
			public NameInfo(string name, NameCreator nameCreator) {
				this.name = name;
				this.nameCreator = nameCreator;
			}
		}

		public void Add(string name, NameCreator nameCreator) {
			nameInfos.Add(new NameInfo(name, nameCreator));
		}

		public NameCreator Find(string typeName) {
			foreach (var nameInfo in nameInfos) {
				if (typeName.Contains(nameInfo.name))
					return nameInfo.nameCreator;
			}

			return null;
		}
	}

	public class TypeNameCreator : ITypeNameCreator {
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
			createUnknownTypeName = CreateNameCreator("Type");
			createEnumName = CreateNameCreator("Enum");
			createStructName = CreateNameCreator("Struct");
			createDelegateName = CreateNameCreator("Delegate");
			createClassName = CreateNameCreator("Class");
			createInterfaceName = CreateNameCreator("Interface");

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
				nameInfos.Add(name, CreateNameCreator(name));
		}

		protected virtual NameCreator CreateNameCreator(string prefix) {
			return new NameCreator(prefix);
		}

		public string Create(TypeDef typeDef, string newBaseTypeName) {
			var nameCreator = GetNameCreator(typeDef, newBaseTypeName);
			return existingNames.GetName(typeDef.Name.String, nameCreator);
		}

		NameCreator GetNameCreator(TypeDef typeDef, string newBaseTypeName) {
			var nameCreator = createUnknownTypeName;
			if (typeDef.IsEnum)
				nameCreator = createEnumName;
			else if (typeDef.IsValueType)
				nameCreator = createStructName;
			else if (typeDef.IsClass) {
				if (typeDef.BaseType != null) {
					var fn = typeDef.BaseType.FullName;
					if (fn == "System.Delegate")
						nameCreator = createDelegateName;
					else if (fn == "System.MulticastDelegate")
						nameCreator = createDelegateName;
					else {
						nameCreator = nameInfos.Find(newBaseTypeName ?? typeDef.BaseType.Name.String);
						if (nameCreator == null)
							nameCreator = createClassName;
					}
				}
				else
					nameCreator = createClassName;
			}
			else if (typeDef.IsInterface)
				nameCreator = createInterfaceName;
			return nameCreator;
		}
	}

	public class GlobalTypeNameCreator : TypeNameCreator {
		public GlobalTypeNameCreator(ExistingNames existingNames)
			: base(existingNames) {
		}

		protected override NameCreator CreateNameCreator(string prefix) {
			return base.CreateNameCreator("G" + prefix);
		}
	}
}
