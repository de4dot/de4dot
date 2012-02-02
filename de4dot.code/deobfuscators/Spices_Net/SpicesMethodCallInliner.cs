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

using Mono.Cecil;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.Spices_Net {
	class SpicesMethodCallInliner : MethodCallInliner {
		public SpicesMethodCallInliner()
			: base(false) {
		}

		public static bool checkCanInline(MethodDefinition method) {
			if (method.Attributes != (MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig))
				return false;
			if (!method.DeclaringType.IsNested)
				return false;
			return true;
		}

		protected override bool canInline(MethodDefinition method) {
			if (!checkCanInline(method))
				return false;

			//TODO: Should only allow certain nested classes here, not all.
			return true;
		}
	}
}
