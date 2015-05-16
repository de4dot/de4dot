/*
    Copyright (C) 2011-2014 de4dot@gmail.com

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

namespace de4dot.code.deobfuscators.Orange_Heap
{
    using de4dot.code.deobfuscators;
    using System;

    public class DeobfuscatorInfo : DeobfuscatorInfoBase
    {
        private const string DEFAULT_REGEX = @"^[\u2E80-\u9FFFa-zA-Z_<{$][\u2E80-\u9FFFa-zA-Z_0-9<>{}$.`-]*$";
        public const string THE_NAME = "Orange Heap";
        public const string THE_TYPE = "oh";

        public DeobfuscatorInfo() : base(@"^[\u2E80-\u9FFFa-zA-Z_<{$][\u2E80-\u9FFFa-zA-Z_0-9<>{}$.`-]*$")
        {
        }

        public override IDeobfuscator CreateDeobfuscator()
        {
            return new Deobfuscator(new Deobfuscator.Options { RenameResourcesInCode = false, ValidNameRegex = base.validNameRegex.Get() });
        }

        public override string Name
        {
            get
            {
                return "Orange Heap";
            }
        }

        public override string Type
        {
            get
            {
                return "oh";
            }
        }
    }
}

