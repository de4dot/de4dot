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
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.deobfuscators.SmartAssembly {
	class ProxyDelegateFinder : ProxyDelegateFinderBase {
		static readonly Dictionary<char, int> specialCharsDict = new Dictionary<char, int>();
		static readonly char[] specialChars = new char[] {
			'\x01', '\x02', '\x03', '\x04', '\x05', '\x06', '\x07', '\x08',
			'\x0E', '\x0F', '\x10', '\x11', '\x12', '\x13', '\x14', '\x15',
			'\x16', '\x17', '\x18', '\x19', '\x1A', '\x1B', '\x1C', '\x1D',
			'\x1E', '\x1F', '\x7F', '\x80', '\x81', '\x82', '\x83', '\x84',
			'\x86', '\x87', '\x88', '\x89', '\x8A', '\x8B', '\x8C', '\x8D',
			'\x8E', '\x8F', '\x90', '\x91', '\x92', '\x93', '\x94', '\x95',
			'\x96', '\x97', '\x98', '\x99', '\x9A', '\x9B', '\x9C', '\x9D',
			'\x9E', '\x9F',
		};

		static ProxyDelegateFinder() {
			for (int i = 0; i < specialChars.Length; i++)
				specialCharsDict[specialChars[i]] = i;
		}

		public ProxyDelegateFinder(ModuleDefinition module, IList<MemberReference> memberReferences)
			: base(module, memberReferences) {
		}

		protected override void getCallInfo(FieldDefinition field, out int methodIndex, out bool isVirtual) {
			isVirtual = false;
			string name = field.Name;

			methodIndex = 0;
			for (int i = name.Length - 1; i >= 0; i--) {
				char c = name[i];
				if (c == '~') {
					isVirtual = true;
					break;
				}

				int val;
				if (specialCharsDict.TryGetValue(c, out val))
					methodIndex = methodIndex * specialChars.Length + val;
			}
		}

		public void findDelegateCreator(ModuleDefinition module) {
			var callCounter = new CallCounter();
			foreach (var type in module.Types) {
				if (type.Namespace != "" || !DotNetUtils.isDelegateType(type))
					continue;
				var cctor = DotNetUtils.getMethod(type, ".cctor");
				if (cctor == null)
					continue;
				foreach (var method in DotNetUtils.getMethodCalls(cctor))
					callCounter.add(method);
			}

			var mostCalls = callCounter.most();
			if (mostCalls == null)
				return;

			setDelegateCreatorMethod(DotNetUtils.getMethod(DotNetUtils.getType(module, mostCalls.DeclaringType), mostCalls));
		}
	}
}
