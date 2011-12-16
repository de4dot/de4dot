
de4dot - Deobfuscator for .NET
==============================

Features
--------

* Supports some popular obfuscators
* Deobfuscates control flow
* Cross-assembly symbol renaming
* Decrypts strings
* Decrypts resources
* Dumps embedded assemblies
* Dumps encrypted methods
* Deobfuscated files are runnable
* Removes other obfuscator junk
* Supports pure managed .NET files only
* Fixes peverify errors created by the obfuscator
* 100% Open Source

Many features work even if it's an unsupported obfuscator but the result may
or may not be runnable.

Who would need a deobfuscator?
------------------------------

* Security experts who need to deobfuscate obfuscated .NET malware.
* You lost your source code but have a copy of your obfuscated .NET
  assemblies. You can then use ILSpy to decompile them.
* You must verify that an obfuscated compiled .NET assembly contains the
  claimed source code, and no backdoors.
* Some obfuscators are not Mono-compatible. If you deobfuscate it, it may run
  on Mono.
* Some obfuscated programs crash on 64-bit Windows. Deobfuscating it and
  removing the obfuscator code solves that problem.
* You can only run verifiable .NET code, but the obfuscated program is
  non-verifiable due to the obfuscated code.
* You don't want string decryption and tons of useless CIL instructions to
  slow down your favorite program.

Features explained
------------------

### Supports some popular obfuscators

I won't list the supported obfuscators since I'd forget to update it when I
add support for another one. Run `de4dot -h` to get a list of the supported
obfuscators. It's usually very easy to add support for another obfuscator.

Other obfuscators are partially supported. Eg. control flow deobfuscation,
symbol renaming, and dynamic string decryption could possibly work.

### Deobfuscates control flow

Most obfuscators can rearrange the control flow so the code is harder to
understand. A simple method that is 10 lines long and easy to read, could
become 30-40 lines and be very hard to read. Control flow deobfuscation will
remove all of the obfuscated code, leaving just the original code. Dead code
is also removed.

### Cross-assembly symbol renaming

Many obfuscators can rename public classes if they're part of a private
assembly. This deobfuscator will properly rename not only the obfuscated class
and all references within that assembly, but also all references in other
assemblies. If you don't need symbol renaming, you should disable it.

### Decrypts strings

Most, if not all, obfuscators support encrypting the strings. They usually
replace the original string with an encrypted string, or an integer. The
encrypted string or integer is then handed over to the string decrypter which
returns the original string. This deobfuscator supports static decryption and
dynamic decryption. Dynamic decryption will load the assembly and then call
the string decrypter and save the decrypted string. You can tell it which
method is the string decrypter and it will do the rest. The default is to use
static string decryption. Dynamic string decryption can be useful if you're
deobfuscating an assembly obfuscated with an unsupported obfuscator.

### Decrypts resources

Resources are usually encrypted by the obfuscator. The deobfuscator supports
static decryption only.

### Dumps embedded assemblies

Some obfuscators can embed assemblies. They usually encrypt and compress the
assembly and put it in the resources section. These assemblies will be
decypted and decompressed and then saved to disk.

### Dumps encrypted methods

Some obfuscators encrypt all methods and only decrypt each method when
requested by the .NET runtime. The methods are statically decrypted and then
deobfuscated.

### Deobfuscated files are runnable

If it's a supported obfuscator, the output is runnable. This is an important
feature. If you can't run the resulting file, it's almost useless. Note that
you may need to resign all modified assemblies that were signed. Some programs
have internal checks for modifications that aren't part of the obfuscator.
These kinds of protections usually cause a crash on purpose. Those aren't
removed since they're part of the original assembly.

### Removes other obfuscator junk

Many obfuscation products add other junk to the file. Eg., they could add code
to log every single exception, or detect deobfuscation, etc. Since that's not
part of the original file, it's also removed. Some obfuscators add so many
junk classes that it's difficult to find all of them, though. You have to
remove those manually for now.

### Supports pure managed .NET files only

This is a limitation of the Mono.Cecil library. Most .NET assemblies, however,
are pure managed .NET assemblies.

### Fixes peverify errors created by the obfuscator

Usually when an obfuscator adds control flow obfuscation, the resulting code
doesn't pass all peverify tests, resulting in an unverifiable assembly. By
removing that obfuscation, and making sure to re-arrange the code blocks in a
certain order, those obfuscator created errors will be gone. If the assembly
was verifiable before obfuscation, it must be verifiable after deobfuscation.

### 100% Open Source

Can't get any better than this!

Examples
--------

Show help:

    de4dot -h

Deobfuscate a few files:

    de4dot file1.exe file2.dll file3.exe

Deobfuscate all files found:

    de4dot -r c:\path1 -ro c:\out

Detect obfuscator recursively:

    de4dot -d -r c:\path1

Deobfuscate and get a detailed log of what was changed:

    de4dot -v file1.exe file2.dll file3.exe > log.txt

Deobfuscate and override string decrypter detection, finding and using all
static methods with string and int args that return a string. A dynamic method
is created and used to call the string decrypter method(s). Make sure you
don't include any non-string decrypter methods or you will get an exception:

    de4dot --default-strtyp delegate --default-strtok "(System.String, System.Int32)" file1.exe file2.dll

Same as above but use a metadata token:

    de4dot --default-strtyp delegate file1.exe --strtok 06000123 file2.dll --strtok 06004567 --strtok 06009ABC

Don't remove obfuscator types, methods, etc:

    de4dot --keep-types file1.exe
