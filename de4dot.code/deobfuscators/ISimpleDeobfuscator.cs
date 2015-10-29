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

using System;
using dnlib.DotNet;

namespace de4dot.code.deobfuscators {
	[Flags]
	public enum SimpleDeobfuscatorFlags : uint {
		Force											= 0x00000001,

		// Hack for Confuser deobfuscator code. That code was written before the
		// constants folder was updated and it now breaks the old Confuser code.
		DisableConstantsFolderExtraInstrs				= 0x00000002,
	}

	public interface ISimpleDeobfuscator {
		void MethodModified(MethodDef method);
		void Deobfuscate(MethodDef method);
		void Deobfuscate(MethodDef method, SimpleDeobfuscatorFlags flags);
		void DecryptStrings(MethodDef method, IDeobfuscator deob);
	}
}
