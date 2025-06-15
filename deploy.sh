#!/bin/bash
git pull
current_version=$(grep -oP '(?<=<Version>).*?(?=</Version>)' SimpleRssServer/SimpleRssServer.fsproj)
echo "Current version: $current_version"
read -p "Enter new version number: " new_version
sed -i "s/<Version>.*<\/Version>/<Version>$new_version<\/Version>/" SimpleRssServer/SimpleRssServer.fsproj
git add SimpleRssServer/SimpleRssServer.fsproj
git commit -m "bump version number"
git tag $new_version
dotnet_version=$(grep -oP '(?<=<TargetFramework>).*?(?=</TargetFramework>)' SimpleRssServer/SimpleRssServer.fsproj)
dotnet publish
sudo systemctl stop rssrdr-server.service
find /var/www/rssrdr/* -not -name 'rss-cache' -not -path '/var/www/rssrdr/rss-cache/*' -exec rm -rf {} +
cp -r SimpleRssServer/bin/Release/$dotnet_version/publish/* /var/www/rssrdr/
sudo systemctl start rssrdr-server.service
git push origin "$new_version"
