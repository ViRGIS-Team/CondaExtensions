# CondaExtensions
 
This UPM Package contains scripts that can be used by other UPM packages to integrate into the [Conda Package Management System](https://docs.conda.io/en/latest/).

# Documentation and Usage

The purpose and usage of this package are eplained in the following article :

[Package Documentation](https://medium.com/runic-software/using-conda-as-a-unity-package-version-2-c3442bf9c245)

# Use with Unity Cloud Build

As of release 1.0.4, this package will work with Unity Cloud Build.

You will need to add a pre-build scipt to your build configuration in Unity Cloud Build to load Miniconda.

For Windows configurations - the following script works well:

https://gist.github.com/nimaid/a7d6d793f2eba4020135208a57f5c532

For Macos and Linux, something like this:

```
echo starting conda install

curl https://repo.anaconda.com/miniconda/Miniconda3-latest-MacOSX-x86_64.sh -o conda.sh
bash conda.sh -b -p ~/local/miniconda3
echo completed conda install
```

Note that for Macos and Linux, the comsuming package's install_script.sh must include the line before the conda commands:

```
export PATH=~/local/miniconda3/bin:$PATH
```
