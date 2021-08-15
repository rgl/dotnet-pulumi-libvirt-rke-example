using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Pulumi;
using Pulumi.Libvirt;
using Pulumi.Libvirt.Inputs;
using Pulumi.Rke;
using Pulumi.Rke.Inputs;

class Stack : Pulumi.Stack
{
    public Stack()
    {
        var controllerCount = 1;
        var workerCount = 1;
        var sshPublicKeyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh/id_rsa.pub");
        var sshPublicKeyJson = JsonSerializer.Serialize(File.ReadAllText(sshPublicKeyPath).Trim());

        var network = new Network("example", new NetworkArgs
        {
            Mode = "nat",
            Domain = "example.test",
            Addresses = new[] {"10.17.3.0/24"},
            Dhcp = new NetworkDhcpArgs
            {
                Enabled = false,
            },
            Dns = new NetworkDnsArgs
            {
                Enabled = true,
                LocalOnly = false,
            },
        });

        Func<string, string, int, Domain> vm = (name, ipAddress, memory) =>
        {
            // create a cloud-init cloud-config.
            // NB this creates an iso image that will be used by the NoCloud cloud-init datasource.
            // see https://www.pulumi.com/docs/reference/pkg/libvirt/cloudinitdisk/
            // see journactl -u cloud-init
            // see /run/cloud-init/*.log
            // see https://cloudinit.readthedocs.io/en/latest/topics/examples.html#disk-setup
            // see https://cloudinit.readthedocs.io/en/latest/topics/datasources/nocloud.html#datasource-nocloud
            var cloudInit = new CloudInitDisk($"{name}-cloud-init", new CloudInitDiskArgs
            {
                UserData = $@"
#cloud-config
fqdn: {name}.example.test
manage_etc_hosts: true
users:
  - name: vagrant
    passwd: '$6$rounds=4096$NQ.EmIrGxn$rTvGsI3WIsix9TjWaDfKrt9tm3aa7SX7pzB.PSjbwtLbsplk1HsVzIrZbXwQNce6wmeJXhCq9YFJHDx9bXFHH.'
    lock_passwd: false
    ssh-authorized-keys:
      - {sshPublicKeyJson}
runcmd:
  - sed -i '/vagrant insecure public key/d' /home/vagrant/.ssh/authorized_keys
",
            });

            var bootVolume = new Volume($"{name}-boot", new VolumeArgs
            {
                BaseVolumeName = "ubuntu-20.04-amd64_vagrant_box_image_0_box.img",
                Format = "qcow2",
                // NB its not yet possible to create larger disks.
                //    see https://github.com/pulumi/pulumi-libvirt/issues/6
                //Size = 66*1024*1024*1024, // 66GiB. the root FS is automatically resized by cloud-init growpart (see https://cloudinit.readthedocs.io/en/latest/topics/examples.html#grow-partitions).
            });

            var domain = new Domain(name, new DomainArgs
            {
                Cpu = new DomainCpuArgs
                {
                    Mode = "host-passthrough",
                },
                Vcpu = 2,
                Memory = 1024,
                QemuAgent = true,
                NetworkInterfaces = new DomainNetworkInterfaceArgs
                {
                    NetworkId = network.Id,
                    WaitForLease = true,
                    Addresses = new[] {ipAddress},
                },
                Cloudinit = cloudInit.Id,
                Disks = new[]
                {
                    new DomainDiskArgs
                    {
                        VolumeId = bootVolume.Id,
                        Scsi = true,
                    },
                },
                Description = $"path: {Environment.CurrentDirectory}\nproject: {Pulumi.Deployment.Instance.ProjectName}\nstack: {Pulumi.Deployment.Instance.StackName}\n",
            });

            return domain;
        };

        var controllers = Enumerable.Range(0, controllerCount)
            .Select(index => vm($"c{index}", $"10.17.3.{10+index}", 2*1024))
            .ToList();

        var workers = Enumerable.Range(0, workerCount)
            .Select(index => vm($"w{index}", $"10.17.3.{20+index}", 2*1024))
            .ToList();

        var cluster = new Cluster("example", new ClusterArgs
        {
            KubernetesVersion = "v1.20.6-rancher1-1",
            Nodes = controllers.Select(n => new ClusterNodeArgs
                {
                    Address = n.NetworkInterfaces.GetAt(0).Apply(n => n.Addresses[0]),
                    User = "vagrant",
                    Roles = new[] {"controlplane", "etcd"},
                    SshKeyPath = sshPublicKeyPath,
                }).Concat(workers.Select(n => new ClusterNodeArgs
                {
                    Address = n.NetworkInterfaces.GetAt(0).Apply(n => n.Addresses[0]),
                    User = "vagrant",
                    Roles = new[] {"worker"},
                    SshKeyPath = sshPublicKeyPath,
                })).ToList(),
            UpgradeStrategy = new ClusterUpgradeStrategyArgs
            {
                Drain = true,
                MaxUnavailableWorker = "20%",
            }
        });

        RkeState = cluster.RkeState;

        KubeConfig = cluster.KubeConfigYaml;
    }

    [Output]
    public Output<string> RkeState { get; set; }

    [Output]
    public Output<string> KubeConfig { get; set; }
}