# PdbComparer
A simple program to compare a rewritten assembly and it's pdb files against the original assembly.

The program is provided as is and I don't have any plans to improve it for the future.

The intended usage was to troubleshoot programs rewritten using CodeContracts where 
sometimes the pdb files have become out of sync and had methods declared in the wrong files.


#Usage

PdbComparer --Source "assembly1.dll" --Actual "assembly_rewritten.dll" --LogLevel Verbose

PdbComparer --Source "assembly1.dll" --Actual "assembly_rewritten.dll" --LogLevel Debug --DisableLineNrComparison
