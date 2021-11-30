# About

**This is barely working because Pulumi has no way to directly execute scripts inside a VM to install the RKE dependencies. Instead we are executing them from cloud-init, which is not a good enough solution, as there is no way to check for errors. We also have no way to wait for cloud-init to finish, which means you have to execute `pulumi up` again after the provisioning script finishes.**

This creates an example RKE cluster in libvirt QEMU/KVM Virtual Machines using dotnet [Pulumi](https://www.pulumi.com/).

**NB** For a terraform equivalent see the [rgl/terraform-libvirt-rke-example](https://github.com/rgl/terraform-libvirt-rke-example) repository and the [rgl/terraform-rke-vsphere-cloud-provider-example](https://github.com/rgl/terraform-rke-vsphere-cloud-provider-example) repository.

## Usage (Ubuntu 20.04 host)

Create and install the [Ubuntu 20.04 vagrant box](https://github.com/rgl/ubuntu-vagrant) (because this example uses its base disk).

Install kubectl:

```bash
kubectl_version='1.20.6'
wget -qO /usr/share/keyrings/kubernetes-archive-keyring.gpg https://packages.cloud.google.com/apt/doc/apt-key.gpg
echo 'deb [signed-by=/usr/share/keyrings/kubernetes-archive-keyring.gpg] https://apt.kubernetes.io/ kubernetes-xenial main' | sudo tee /etc/apt/sources.list.d/kubernetes.list >/dev/null
sudo apt-get update
kubectl_package_version="$(apt-cache madison kubectl | awk "/$kubectl_version-/{print \$3}")"
sudo apt-get install -y "kubectl=$kubectl_package_version"
```

[Install the dotnet 6.0 SDK](https://docs.microsoft.com/en-us/dotnet/core/install/linux-ubuntu):

```bash
echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1' >/etc/profile.d/opt-out-dotnet-cli-telemetry.sh
source /etc/profile.d/opt-out-dotnet-cli-telemetry.sh
wget -qO packages-microsoft-prod.deb "https://packages.microsoft.com/config/ubuntu/$(lsb_release -s -r)/packages-microsoft-prod.deb"
dpkg -i packages-microsoft-prod.deb
apt-get install -y apt-transport-https
apt-get update
apt-get install -y dotnet-sdk-6.0
```

[Install Pulumi](https://www.pulumi.com/docs/get-started/install/):

```bash
wget https://get.pulumi.com/releases/sdk/pulumi-v3.18.1-linux-x64.tar.gz
sudo tar xf pulumi-v3.18.1-linux-x64.tar.gz -C /usr/local/bin --strip-components 1
rm pulumi-v3.18.1-linux-x64.tar.gz
```

Configure the stack:

```bash
cat >secrets.sh <<'EOF'
export PULUMI_SKIP_UPDATE_CHECK=true
export PULUMI_BACKEND_URL="file://$PWD" # NB pulumi will create the .pulumi sub-directory.
export PULUMI_CONFIG_PASSPHRASE='password'
EOF
```

Launch this example:

```bash
source secrets.sh
pulumi login
pulumi whoami -v
# NB this will fail until provision.sh (executed by cloud-init) installs the
#    RKE dependencies. you have to execute it again after cloud-init finishes
#    (look at the machine console to known when it finishes).
pulumi up
```

Test accessing the cluster:

```bash
pulumi stack select
pulumi stack output --show-secrets RkeState >rkestate.json # useful for troubleshooting.
pulumi stack output --show-secrets KubeConfig >kubeconfig.yaml
export KUBECONFIG=$PWD/kubeconfig.yaml
kubectl get nodes -o wide
```

Destroy everything:

```bash
pulumi destroy
```

## Notes

* There is not yet a built-in way to execute ad-hoc provision commands à-la
  terraform `remote-exec`.
  * see https://github.com/pulumi/pulumi/issues/99
  * see https://github.com/pulumi/pulumi/issues/1691
  * see https://github.com/pulumi/examples/tree/master/aws-ts-ec2-provisioners

## References

* https://www.pulumi.com/docs/intro/cloud-providers/rke/
* https://www.pulumi.com/docs/reference/pkg/rke/
* https://www.pulumi.com/docs/intro/cloud-providers/libvirt/
* https://www.pulumi.com/docs/reference/pkg/libvirt/
* https://github.com/pulumi/pulumi-libvirt
* https://github.com/pulumi/examples/
