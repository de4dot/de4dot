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

namespace de4dot.deobfuscators {
	interface IDeobfuscatorOptions {
		bool RenameResourcesInCode { get; }
	}

	[Flags]
	enum StringFeatures {
		AllowNoDecryption,
		AllowStaticDecryption,
		AllowDynamicDecryption,
		AllowAll = AllowNoDecryption | AllowStaticDecryption | AllowDynamicDecryption,
	}

	interface IDeobfuscator {
		string Type { get; }
		string Name { get; }
		Func<string, bool> IsValidName { get; }
		IDeobfuscatorOptions TheOptions { get; }
		IOperations Operations { get; set; }
		StringFeatures StringFeatures { get; set; }

		// This is non-null only in init(), detect() and deobfuscateBegin().
		IDeobfuscatedFile DeobfuscatedFile { get; set; }

		void init(ModuleDefinition module, IList<MemberReference> memberReferences);

		// Returns 0 if it's not detected, or > 0 if detected (higher value => more likely true)
		int detect();

		// Called before all other deobfuscation methods
		void deobfuscateBegin();

		// Called before the code is deobfuscated
		void deobfuscateMethodBegin(Blocks blocks);

		// Called after deobfuscateMethodBegin() but before deobfuscateMethodEnd()
		void deobfuscateStrings(Blocks blocks);

		// Called after the code has been deobfuscated
		void deobfuscateMethodEnd(Blocks blocks);

		// Called after all deobfuscation methods
		void deobfuscateEnd();

		// Called to get method token / pattern of string decrypters
		IEnumerable<string> getStringDecrypterMethods();
	}
}
