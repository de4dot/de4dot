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
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class CoMethodCallInliner : MethodCallInliner {
		readonly InlinedMethodTypes inlinedMethodTypes;

		public CoMethodCallInliner(InlinedMethodTypes inlinedMethodTypes)
			: base(false) {
			this.inlinedMethodTypes = inlinedMethodTypes;
		}

		protected override bool CanInline(MethodDef method) {
			if (method == null)
				return false;

			if (method.Attributes != (MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig))
				return false;
			if (method.HasGenericParameters)
				return false;
			if (!InlinedMethodTypes.IsValidMethodType(method.DeclaringType))
				return false;

			return true;
		}

		protected override void OnInlinedMethod(MethodDef methodToInline, bool inlinedMethod) {
			if (inlinedMethod)
				inlinedMethodTypes.Add(methodToInline.DeclaringType);
			else
				inlinedMethodTypes.DontRemoveType(methodToInline.DeclaringType);
		}
	}
}
