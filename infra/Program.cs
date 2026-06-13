using System.Collections.Generic;
using Pulumi;
using Hcloud = Pulumi.HCloud;

return await Deployment.RunAsync(() =>
{
    var cfg = new Config();
    var serverType = cfg.Get("serverType") ?? "cax11";          // ARM, 2 vCPU / 4 GB — plenty
    var location   = cfg.Get("location")   ?? "nbg1";           // Nuremberg
    var image      = cfg.Get("image")      ?? "ubuntu-24.04";
    var domain     = cfg.Get("domain")     ?? "";               // empty ⇒ plain HTTP on the IP
    var repoUrl    = cfg.Get("repoUrl")    ?? "https://github.com/Sossenbinder/OsrsArbitrage.git";
    var sshPublicKey = cfg.Require("sshPublicKey");             // contents of your *.pub key

    var sshKey = new Hcloud.SshKey("deploy-key", new()
    {
        PublicKey = sshPublicKey,
    });

    var firewall = new Hcloud.Firewall("web-fw", new()
    {
        Rules =
        {
            new Hcloud.Inputs.FirewallRuleArgs { Direction = "in", Protocol = "tcp", Port = "22",  SourceIps = { "0.0.0.0/0", "::/0" } },
            new Hcloud.Inputs.FirewallRuleArgs { Direction = "in", Protocol = "tcp", Port = "80",  SourceIps = { "0.0.0.0/0", "::/0" } },
            new Hcloud.Inputs.FirewallRuleArgs { Direction = "in", Protocol = "tcp", Port = "443", SourceIps = { "0.0.0.0/0", "::/0" } },
        },
    });

    // Day-0 bootstrap: install Docker, clone the repo, write .env, bring the stack up.
    // Day-N updates are handled by the Deploy GitHub workflow (pull image + restart).
    var cloudInit = $@"#cloud-config
package_update: true
runcmd:
  - curl -fsSL https://get.docker.com | sh
  - rm -rf /opt/app && git clone {repoUrl} /opt/app
  - printf 'DOMAIN=%s\n' '{domain}' > /opt/app/deploy/.env
  - cd /opt/app/deploy && docker compose up -d
";

    var server = new Hcloud.Server("osrs-arb", new()
    {
        ServerType = serverType,
        Image = image,
        Location = location,
        SshKeys = { sshKey.Name },
        FirewallIds = { firewall.Id.Apply(id => int.Parse(id)) },
        UserData = cloudInit,
    });

    return new Dictionary<string, object?>
    {
        ["serverIp"]   = server.Ipv4Address,
        ["sshCommand"] = server.Ipv4Address.Apply(ip => $"ssh root@{ip}"),
        ["url"]        = string.IsNullOrEmpty(domain)
            ? server.Ipv4Address.Apply(ip => $"http://{ip}")
            : Output.Create($"https://{domain}"),
    };
});
