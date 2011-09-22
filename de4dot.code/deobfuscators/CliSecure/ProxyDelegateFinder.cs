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

namespace de4dot.deobfuscators.CliSecure {
	class ProxyDelegateFinder : ProxyDelegateFinderBase {
		public ProxyDelegateFinder(ModuleDefinition module, IList<MemberReference> memberReferences)
			: base(module, memberReferences) {
		}

		protected override void getCallInfo(FieldDefinition field, out int methodIndex, out bool isVirtual) {
			var name = field.Name;
			isVirtual = false;
			if (name.EndsWith("%", StringComparison.Ordinal)) {
				isVirtual = true;
				name = name.TrimEnd(new char[] { '%' });
			}
			byte[] value = Convert.FromBase64String(name);
			methodIndex = BitConverter.ToInt32(value, 0);	// 0-based memberRef index
		}
	}
}
