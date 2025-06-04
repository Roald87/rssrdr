#!/bin/bash
git pull
read -p "Enter new version number: " new_version
sed -i "s/<Version>.*<\/Version>/<Version>$new_version<\/Version>/" SimpleRssServer/SimpleRssServer.fsproj
git add SimpleRssServer/SimpleRssServer.fsproj
git commit -m "bump version number"
git tag -a "$new_version" -m "Version $new_version"
dotnet publish
sudo systemctl stop rssrdr-server.service
find /var/www/rssrdr/* -not -name 'rss-cache' -not -path '/var/www/rssrdr/rss-cache/*' -exec rm -rf {} +
cp -r SimpleRssServer/bin/Release/net8.0/publish/* /var/www/rssrdr/
sudo systemctl start rssrdr-server.service
git push
git push origin "$new_version"
