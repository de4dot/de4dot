@echo off

msbuild -v:m -restore -t:Build -p:Configuration=Release -p:TargetFramework=net35 de4dot.netframework.sln || goto :error
del Release\net35\*.pdb Release\net35\*.xml Release\net35\Test.Rename.* || goto :error

dotnet publish -c Release -f netcoreapp2.1 -o publish-netcoreapp2.1 de4dot || goto :error
del publish-netcoreapp2.1\*.pdb || goto :error
del publish-netcoreapp2.1\*.xml || goto :error
dotnet publish -c Release -f netcoreapp3.0 -o publish-netcoreapp3.0 de4dot || goto :error
del publish-netcoreapp3.0\*.pdb || goto :error
del publish-netcoreapp3.0\*.xml || goto :error

exit /b 0

goto :EOF

:error
exit /b %errorlevel%
