# rssrdr

_Pure RSS reading without the bloat. Device-agnostic, registration-free, and radically simple._

![screenshot of the RSS reader with three feeds from seth's blog and spectrum.ieee.com](website.png)

The simplest RSS feed aggregator on the web.

## Feed inspiration

For inspiration of RSS feeds, see the [most popular links posted on Hackernews](inspiration/hn-links.tsv) or the [most popular blogs posted on Hackernews](inspiration/hn-blogs.tsv), between 26 August 2023 - 26 August 2024.

## Developers

To install the project
- `dotnet restore`

To run the unit test
- `cd SimpleRssServer.Tests`
- `dotnet test`

Starting the server. You can watch it at 127.0.0.1:5000
- `cd SimpleRssServer`
- `dotnet watch`

### Initial setup on Linux server

1. Copy the service configuration for the webserver from `./server-config/`, assuming you're in this top folder of this repo.
    - `sudo cp -i ./server-conf/rssrdr-server.service /etc/systemd/system/rssrdr-server.service`

1. Create the logging folders
    - `sudo mkdir -p /var/log/rssrdr-server`
    - `sudo chown rss:1000 /var/log/rssrdr-server`

1. Create the folder for the binaries. This is where the executable for the server is going to be.
    - `sudo mkdir /var/www/rssrdr`
    - `sudo chown -R rss:1000 /var/www/rssrdr`

1. After creating the service file, reload the systemctl manager configuration to recognize the new service:
    - `sudo systemctl daemon-reload`

1. Enable the service to start automatically at boot:
    - `sudo systemctl enable rssrdr-server.service`

1. Start the service immediately:
    - `sudo systemctl start rssrdr-server.service`

1. Check if the service is running:
    - `sudo systemctl status rssrdr-server.service`

1. Check the logs.
    View the logs (stdout) using:
        - `sudo tail -f /var/log/rssrdr-server/rssrdr.log`

    View the error logs (stderr) using:
        - `sudo tail -f /var/log/rssrdr-server/rssrdr.err`

### Deploying

To deploy, assuming the repo is cloned in `~/rssrdr/` and the setup above is done:
- `~/rssrdr/deploy.sh`

### Enabling HTTPS

#### Certificates

Certificates are created using https://certbot.eff.org/. Follow the instructions there to generate a certificate.

- Certificat is created with: `certbot --nginx -d rssrdr.com`
- Certificate is saved at: `/etc/letsencrypt/live/rssrdr.com/fullchain.pem`
- Key is saved at: `/etc/letsencrypt/live/rssrdr.com/privkey.pem`

#### nginx setup

> [!NOTE]
> .NET's `HttpClient` doesn't support HTTPS on Linux. Therefore we need nginx as a front end with SSL and the SimpleRssServer as non-SSL. See also this [GitHub discussion](https://github.com/dotnet/WatsonWebserver/discussions/90).

Setup

```
 --Client--             ---------Server--------------
|          |           |                             |
| Browser  | --------> | nginx  -->  SimpleRssServer |
|          |  Request  | :443        :8000           |
 ----------             -----------------------------
```

1. Install nginx according to the [instructions](http://nginx.org/en/linux_packages.html)
2. Copy content of `./server-conf/nginx.conf` to `/etc/nginx/nginx.conf`.
3. Start nginx
    - `nginx`

### Server paths overview

ngnix
- config: `/etc/nginx/nginx.conf`
- logs: `/var/log/nginx`

rssrdr-server
- service: `/etc/systemd/system/rssrdr-server.service`
- logs: `/var/log/rssrdr-server/`
- source code: `~/rssrdr`
- binaries: `/var/www/rssrdr`
