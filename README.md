# ModelCreator

A controlled local Tekla Structures model creator for Severfield.

## Prototype (Tekla 2026 only)

C# 7.3, WPF, .NET Framework 4.8, Visual Studio 2022. Open `ModelCreator.sln`.

Enter a contract number (currently `C` followed by four digits) and a project name.
The model name is generated as `C1234-Project Name`, with a maximum of 40 characters.

The Create Model button:

1. Validates the name and checks for a conflicting destination.
2. Checks that the Tekla 2026 executable exists.
3. Creates `C:\TeklaStructuresModels2026` if necessary.
4. Starts Tekla using `/create:"<full model path>" /modelTemplate:"SEVERFIELD_Model_Template"`.

**The first prototype uses Tekla's documented command-line switches rather than an Open API dependency.** The launch returning successfully does **not** prove model creation has completed; verify the model in Tekla. The user must have the approved Severfield environment, role and template configured in Tekla.

The executable path currently defaults to `C:\Program Files\Tekla Structures\2026.0\bin\TeklaStructures.exe`. If the installation differs, adjust `Services/TeklaModelLauncher.cs`.

### Excluded from this prototype

Project property/UDA stamping, Workspace, Tekla Model Sharing, and Trimble Connect integration. Future contract selection should be supplied by a contract provider instead of handwritten values.

### References

- [Tekla 2026 startup arguments](https://support.tekla.com/doc/tekla-structures/2026/cus_create_startup_shortcuts_with_customized_initializations)
- [Tekla 2026 model creation guidance](https://support.tekla.com/doc/tekla-structures/2026/mod_creating_a_new_model)
