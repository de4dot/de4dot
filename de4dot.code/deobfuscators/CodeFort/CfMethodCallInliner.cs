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

using dnlib.DotNet;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.CodeFort {
	class CfMethodCallInliner : MethodCallInliner {
		ProxyCallFixer proxyCallFixer;

		public CfMethodCallInliner(ProxyCallFixer proxyCallFixer)
			: base(false) {
			this.proxyCallFixer = proxyCallFixer;
		}

		protected override bool CanInline(MethodDef method) {
			return proxyCallFixer.IsProxyTargetMethod(method);
		}

		protected override bool IsCompatibleType(int paramIndex, IType origType, IType newType) {
			return true;
		}
	}
}
