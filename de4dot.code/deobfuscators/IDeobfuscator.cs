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
using Mono.MyStuff;
using de4dot.blocks;
using de4dot.renamer;

namespace de4dot.deobfuscators {
	interface IDeobfuscatorOptions {
		bool RenameResourcesInCode { get; }
	}

	public enum DecrypterType {
		Default,
		None,
		Static,
		Delegate,
		Emulate,
	}

	[Flags]
	enum StringFeatures {
		AllowNoDecryption = 1,
		AllowStaticDecryption = 2,
		AllowDynamicDecryption = 4,
		AllowAll = AllowNoDecryption | AllowStaticDecryption | AllowDynamicDecryption,
	}

	[Flags]
	enum RenamingOptions {
		RemoveNamespaceIfOneType = 1,
	}

	interface IDeobfuscator : INameChecker {
		string Type { get; }
		string TypeLong { get; }
		string Name { get; }
		IDeobfuscatorOptions TheOptions { get; }
		IOperations Operations { get; set; }
		StringFeatures StringFeatures { get; }
		RenamingOptions RenamingOptions { get; }
		DecrypterType DefaultDecrypterType { get; }

		// This is non-null only in detect() and deobfuscateBegin().
		IDeobfuscatedFile DeobfuscatedFile { get; set; }

		// Return true if methods can be inlined
		bool CanInlineMethods { get; }

		void init(ModuleDefinition module);

		// Same as detect() but may be used by deobfuscators to detect obfuscator that decrypt
		// metadata at runtime. Code in detect() assume they can access everything. 0 should be
		// returned if not detected.
		int earlyDetect();

		// Returns 0 if it's not detected, or > 0 if detected (higher value => more likely true).
		// This method is always called.
		int detect();

		// If the obfuscator has encrypted parts of the file, then this method should return the
		// decrypted file. true is returned if args have been initialized, false otherwise.
		bool getDecryptedModule(ref byte[] newFileData, ref Dictionary<uint, DumpedMethod> dumpedMethods);

		// This is only called if getDecryptedModule() != null, and after the module has been
		// reloaded. Should return a new IDeobfuscator with the same options and the new module.
		IDeobfuscator moduleReloaded(ModuleDefinition module);

		// Called before all other deobfuscation methods
		void deobfuscateBegin();

		// Called before the code is deobfuscated
		void deobfuscateMethodBegin(Blocks blocks);

		// Return true if we should deobfuscate control flow again
		bool deobfuscateOther(Blocks blocks);

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
